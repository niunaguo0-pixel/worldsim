import re, os
base = "design/gdd"
files = {
 "S4": f"{base}/time-progression.md",
 "S3": f"{base}/civilization-system.md",
 "S2": f"{base}/ecology-sim-engine.md",
 "S1": f"{base}/intervention-system.md",
 "概念": "design/concept/game-concept.md",
 "索引": f"{base}/systems-index.md",
}
for name, p in files.items():
    s = open(p, encoding="utf-8").read()
    m = re.search(r"^版本:\s*(.+)$", s, re.M)
    print(f"[{name}] {p} -> 版本 {m.group(1) if m else '??'}")
print("--- 关键标记（计数）---")
checks = {
 "S4 连续/固定步长/尺度跃迁/世代传承": (files["S4"], ["连续时间","固定步长","尺度跃迁","世代传承"]),
 "S3 都市圈": (files["S3"], ["都市圈"]),
 "S3 §2.12 国家/政体聚合": (files["S3"], ["国家/政体聚合","2.12"]),
 "S3 Polity/NationState": (files["S3"], ["Polity","NationState"]),
 "S3 残留 回合制/每月tick/以月为tick(应0)": (files["S3"], ["回合制","每月 tick","以月为 tick"]),
 "S3 时代门槛 500000(一致性修正)": (files["S3"], ["500000"]),
 "S2 HomeostasisZone(应>0,机制未动)": (files["S2"], ["HomeostasisZone"]),
 "S2 连续时间语言": (files["S2"], ["连续时间"]),
 "S1 pending队列/异步": (files["S1"], ["pending 队列","异步"]),
 "索引 R13/R14": (files["索引"], ["R13","R14"]),
}
for label,(p,kws) in checks.items():
    s = open(p, encoding="utf-8").read()
    cnt = {k: s.count(k) for k in kws}
    print(f"{label}: {cnt}")
