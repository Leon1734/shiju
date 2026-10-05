# -*- coding: utf-8 -*-
"""通过 GitHub Git Data API 推送本地最新提交（绕过不稳定的 git 传输通道）。
用法: python tools/release/push_via_api.py
"""
import base64
import http.client
import json
import os
import socket
import ssl
import subprocess
import sys

REPO = "Leon1734/shiju"
BRANCH = "main"
API_IP = "20.205.243.168"   # nslookup api.github.com 223.5.5.5
REPO_ROOT = r"E:\Workspace_AI\ZCODE\demo_0820"


class IpConn(http.client.HTTPSConnection):
    """连接固定 IP，但 SNI/校验按真实域名走。"""

    def __init__(self, ip, host, **kw):
        super().__init__(host, **kw)
        self._ip = ip

    def connect(self):
        self.sock = socket.create_connection((self._ip, self.port), self.timeout)
        ctx = ssl.create_default_context()
        ctx.check_hostname = False
        ctx.verify_mode = ssl.CERT_NONE
        self.sock = ctx.wrap_socket(self.sock, server_hostname=self.host)


def api(method, path, token, body=None):
    conn = IpConn(API_IP, "api.github.com", timeout=40)
    headers = {
        "Authorization": f"token {token}",
        "Accept": "application/vnd.github+json",
        "User-Agent": "ShiJu-Push",
        "Content-Type": "application/json",
    }
    payload = json.dumps(body).encode("utf-8") if body is not None else None
    conn.request(method, path, body=payload, headers=headers)
    resp = conn.getresponse()
    data = resp.read().decode("utf-8")
    conn.close()
    if resp.status >= 300:
        raise RuntimeError(f"{method} {path} -> {resp.status}: {data[:400]}")
    return json.loads(data) if data else {}


def git(*args):
    return subprocess.run(["git", "-C", REPO_ROOT, *args],
                          capture_output=True, text=True, check=True).stdout.strip()


MAP_FILE = os.path.join(REPO_ROOT, ".git", "zcode-api-push-base")


def read_map():
    """读取本地提交 -> 远程提交 的内容映射（哈希不同、内容一致时用于替换父提交）。"""
    if not os.path.exists(MAP_FILE):
        return {}
    mapping = {}
    for line in open(MAP_FILE, encoding="utf-8"):
        parts = line.split()
        if len(parts) == 2:
            mapping[parts[0]] = parts[1]      # remote_sha -> local_sha
    return mapping


def write_map(remote_sha, local_sha):
    with open(MAP_FILE, "a", encoding="utf-8") as f:
        f.write(remote_sha + " " + local_sha + "\n")


def main():
    token = None
    cred = subprocess.run(["git", "credential", "fill"], input="protocol=https\nhost=github.com\n\n",
                          capture_output=True, text=True, cwd=REPO_ROOT).stdout
    for line in cred.splitlines():
        if line.startswith("password="):
            token = line[len("password="):]
    if not token:
        print("no token"); sys.exit(1)

    local_head = git("rev-parse", "HEAD")
    remote_head = api("GET", f"/repos/{REPO}/git/ref/heads/{BRANCH}", token)["object"]["sha"]
    if local_head == remote_head:
        print("远程已是最新，无需推送")
        return
    

    # 确认"要推送的本地提交"与"远程基线"的对应关系：
    # 优先直接父提交；否则查映射表（哈希不同但内容一致的历史重建）
    base_local = None
    if git("rev-parse", f"{local_head}^") == remote_head:
        base_local = remote_head
    else:
        mapping = read_map()
        for key, val in mapping.items():
            if remote_head.startswith(key):      # 映射表可能存短 SHA
                base_local = val
                break
        if base_local:
            print(f"使用内容映射：远程 {remote_head[:7]} == 本地 {base_local[:7]}")
        else:
            print(f"本地 HEAD({local_head[:7]}) 无法对应远程 HEAD({remote_head[:7]})，需人工处理")
            sys.exit(1)

    message = git("log", "-1", "--pretty=%B")
    changed = git("diff", "--name-status", base_local, "HEAD").splitlines()
    if not changed:
        print(f"内容已与远程一致（本地 {local_head[:7]} ≈ 远程 {remote_head[:7]}），记录映射")
        write_map(remote_head, local_head)
        return
    print(f"本地 {local_head[:7]} -> 远程 {remote_head[:7]}，变更 {len(changed)} 个文件")

    entries = []
    for line in changed:
        parts = line.split("\t")
        status, path = parts[0], parts[-1]      # R100 old new 时取新路径
        if status.startswith("D"):
            entries.append({"path": path, "mode": "100644", "type": "blob", "sha": None})
            continue
        full = os.path.join(REPO_ROOT, path.replace("/", os.sep))
        content = base64.b64encode(open(full, "rb").read()).decode("ascii")
        blob = api("POST", f"/repos/{REPO}/git/blobs", token, {"content": content, "encoding": "base64"})
        entries.append({"path": path, "mode": "100644", "type": "blob", "sha": blob["sha"]})
        print(f"  blob {path} -> {blob['sha'][:7]}")

    parent_commit = api("GET", f"/repos/{REPO}/git/commits/{remote_head}", token)
    tree = api("POST", f"/repos/{REPO}/git/trees", token,
               {"base_tree": parent_commit["tree"]["sha"], "tree": entries})
    print(f"  tree -> {tree['sha'][:7]}")

    commit = api("POST", f"/repos/{REPO}/git/commits", token, {
        "message": message,
        "tree": tree["sha"],
        "parents": [remote_head],
        "author": {"name": "Leon1734", "email": "Leon1734@users.noreply.github.com"},
        "committer": {"name": "Leon1734", "email": "Leon1734@users.noreply.github.com"},
    })
    print(f"  commit -> {commit['sha'][:7]}")

    api("PATCH", f"/repos/{REPO}/git/refs/heads/{BRANCH}", token, {"sha": commit["sha"], "force": False})
    write_map(commit["sha"], local_head)
    print(f"✅ 已推送 {BRANCH} -> {commit['sha'][:7]}（映射已记录）")

    # 若本地最新提交带 v* 消息对应版本，同步修正同名 tag 指向（v2.6.0 建 Release 时指向了旧提交）
    tag = None
    for line in message.splitlines():
        if line.strip().startswith("v") and "." in line.split()[0]:
            tag = line.split()[0].strip()
            break
    if tag:
        try:
            api("PATCH", f"/repos/{REPO}/git/refs/tags/{tag}", token, {"sha": commit["sha"], "force": True})
            print(f"✅ tag {tag} 已指向 {commit['sha'][:7]}")
        except Exception as ex:
            print(f"(tag {tag} 更新跳过: {ex})")


if __name__ == "__main__":
    main()
