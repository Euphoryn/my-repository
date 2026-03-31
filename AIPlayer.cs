using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;
using static GameManager;

[System.Serializable]
public class AIPlayer : Player
{
    private readonly float attackBias;
    private readonly float defenseBias;

    // 上下文字段
    private Card currentUsingCard = null;
    private int tarx = -999, tary = -999, tarf = -1;
    private Piece tar;
    private bool isExecutingAttackBehavior = false;

    // 防御标记（不显示标记）
    private HashSet<Piece> defenseMarked = new HashSet<Piece>();

    public AIPlayer(int team, float attackBias, float defenseBias) : base(team)
    {
        this.attackBias = attackBias;
        this.defenseBias = defenseBias;
    }

    public override async UniTask OnMyTurn(int initialCommandCount)
    {
        GameManager.Instance.curPlayer = this;
        UIManager.Instance.ShowPlayerSwitch(this);
        CommandCount = initialCommandCount;

        foreach (Piece x in onBoardList)
        {
            x.OnTurnBegin();
            x.pieceRenderer.UpdateData();
        }

        // 一、胜利判定
        if (await TryWinCondition()) return;

        // 二、使用“无中生有”
        if (HasCard("无中生有"))
        {
            await UseCardWithContext(GetCard("无中生有"));
        }

        // 三、执行攻击行为 + 防御行为（都执行！）
        bool shouldAttack = DecidePrimaryStrategy();
        if (shouldAttack)
        {
            await ExecuteAttackBehavior();
            await ExecuteDefenseBehavior();
        }
        else
        {
            await ExecuteDefenseBehavior();
            await ExecuteAttackBehavior();
        }

        int las = CommandCount;
        while (CommandCount > 0)
        {
            // 五、普通从者行动
            ApplyDefenseMarks();
            await ExecuteNormalServantActions();
            // 六、部署逻辑
            await ExecuteDeploymentLogic();
            // 七、甩技能
            await ExecuteUseSkillLogic();
            if (las == CommandCount)RefreshCard();
            las = CommandCount;
        }
        await ExecuteNormalServantActions();
    }

    private bool HasCard(string name) => hand.Any(c => c.cardName == name);
    private Card GetCard(string name) => hand.FirstOrDefault(c => c.cardName == name);

    private async UniTask<bool> UseCardWithContext(Card card)
    {
        if (CommandCount <= 0) return false;
        if (card == null) return false;
        currentUsingCard = card;

        //Debug.Log($"UseCard {card.cardName} with CommandCount={CommandCount}");
        GameObject go;
        if (card is Piece)
            go = GameObject.Instantiate(UIManager.Instance.UIPiecePrefab, UIManager.Instance.canvas);
        else go = GameObject.Instantiate(UIManager.Instance.UICardPrefab, UIManager.Instance.canvas);
        UIRenderer rend = go.GetComponent<UIRenderer>();
        rend.data = card;rend.pos = 0;
        rend.InitSprite();
        rend.rect.anchoredPosition = new Vector2(-725, 750);
        go.SetActive(true);

        int id = 0;
        for (int i = 0; i < 4; i++)
            if (hand[i] == card) { id = i; break; }
        await base.UseCard(id);
        await UniTask.Delay(1000);

        GameObject.Destroy(go);
        currentUsingCard = null;
        return true;
    }

    // ========================
    // 胜利判定（添加棋盘块）
    // ========================
    private async UniTask<bool> TryWinCondition()
    {
        if (!HasCard("添加棋盘块")) return false;
        if (GameManager.Instance.tiles.Count < 70) return false;
        if (!HasCard("无懈可击")) return false;

        int myHP = master.HP;
        bool isHighest = GameManager.Instance.players.All(p => p.master.HP <= myHP);
        if (!isHighest) return false;

        return await UseCardWithContext(GetCard("添加棋盘块"));
    }

    // ========================
    // 获取敌方单位
    // ========================
    private List<Piece> GetAllEnemies()
    {
        var enemies = new List<Piece>();
        foreach (var tile in GameManager.Instance.tiles.Values)
        {
            if (tile.onTile != null && tile.onTile.player.team != this.team)
            {
                enemies.Add(tile.onTile);
                if (tile.onTile is LoadAble ve)
                    enemies.AddRange(ve.onLoad.OfType<Piece>());
            }
        }
        return enemies;
    }

    // ========================
    // 攻击行为目标
    // ========================
    private List<Piece> GetAttackCoreTargets()
    {
        var enemies = GetAllEnemies();
        return enemies.Where(IsAttackCoreTarget)
                      .OrderByDescending(t => GetAttackTargetPriority(t))
                      .ToList();
    }

    private bool IsAttackCoreTarget(Piece p)
    {
        if (p is Arthuria) return true;
        if (p is Saber sa && sa.equip?.cardName == "遥远的理想乡") return true;
        if (p is Master || p is Golem) return true;
        if (p is Include || p is Sunsettia) return true;
        if (p is Servant s && IsGlobalBuffGear(s.equip?.cardName)) return true;
        return false;
    }

    private int GetAttackTargetPriority(Piece p)
    {
        if (p is Arthuria) return 10;
        if (p is Saber sa && sa.equip?.cardName == "遥远的理想乡") return 9;
        if (p is Master) return 8;
        if (p is Golem) return 7;
        if (p is Include || p is Sunsettia) return 6;
        if (p is Servant s && IsGlobalBuffGear(s.equip?.cardName)) return 5;
        return 0;
    }

    // ========================
    // 防御行为目标
    // ========================
    private List<Piece> GetDefenseCoreTargets()
    {
        var enemies = GetAllEnemies();
        return enemies.Where(IsDefenseCoreTarget)
                      .OrderByDescending(t => GetDefenseTargetPriority(t))
                      .ToList();
    }

    private bool IsDefenseCoreTarget(Piece p)
    {
        if (p is Include || p is Sunsettia) return true;
        if (p is Arthuria) return true;
        if (p is Glider) return true;
        if (p is Saber sa && sa.equip?.cardName == "誓约胜利之剑") return true;
        if (p is Berserker b && b.equip?.cardName == "十二试炼") return true;
        if (p is Servant s && IsGlobalBuffGear(s.equip?.cardName)) return true;
        return false;
    }

    private int GetDefenseTargetPriority(Piece p)
    {
        if (p is Include || p is Sunsettia) return 10;
        if (p is Arthuria) return 9;
        if (p is Glider) return 7;
        if (p is Saber sa && sa.equip?.cardName == "誓约胜利之剑") return 6;
        if (p is Berserker b && b.equip?.cardName == "十二试炼") return 6;
        if (p is Servant s && IsGlobalBuffGear(s.equip?.cardName)) return 5;
        return 0;
    }

    private bool IsGlobalBuffGear(string gearName) =>
        gearName is "无限剑制" or "鲜血神殿" or "遥远的理想乡";

    // ========================
    // 地形杀判定
    // ========================
    private bool CanJuFengTerrainKill(Piece p, int blowDir)
    {
        List<Tile> validTiles = new List<Tile>();
        for (int i = 0; i < 6; i++)
        {
            if (i == blowDir || i == (blowDir + 3) % 6) continue;
            Tile t = GameManager.Instance.GetTile(p.xpos + Piece.dx[i], p.ypos + Piece.dy[i]);
            if (t != null && t.onTile == null)
            {
                if (p.canSwim > 0 || t.type != Terrain.Water)
                    return false;
            }
        }
        return true;
    }

    private bool CanGunMuTerrainKill(Piece p, int blowdir)
    {
        Tile t = GameManager.Instance.GetTile(p.xpos + Piece.dx[blowdir], p.ypos + Piece.dy[blowdir]);
        if (t == null) return true;
        if (t.type == Terrain.Water && p.canSwim <= 0) return true;
        return false;
    }

    // ========================
    // 行为逻辑
    // ========================
    private async UniTask ExecuteAttackBehavior()
    {
        isExecutingAttackBehavior = true;
        var targets = GetAttackCoreTargets();

        // 飓风地形杀（攻击行为）
        if (HasCard("飓风"))
        {
            bool ok = false;
            foreach (var target in targets.Where(t => t is Arthuria || (t is Saber s && s.equip?.cardName == "遥远的理想乡")))
            {
                for (int dir = 0; dir < 6; dir++)
                {
                    if (CanJuFengTerrainKill(target, dir))
                    {
                        int startX = target.xpos - 3 * Piece.dx[dir];
                        int startY = target.ypos - 3 * Piece.dy[dir];
                        tarx = startX;
                        tary = startY;
                        tarf = dir;
                        if (await UseCardWithContext(GetCard("飓风"))) { ok = true; break; }
                    }
                }
                if (ok) break;
            }
        }

        if (HasCard("滚木"))
        {
            bool ok = false;
            foreach (var target in targets.Where(t => t is Arthuria || (t is Saber s && s.equip?.cardName == "遥远的理想乡")))
            {
                for (int dir = 0; dir < 6; dir++)
                {
                    if (CanGunMuTerrainKill(target, dir))
                    {
                        int startX = target.xpos - 3 * Piece.dx[dir];
                        int startY = target.ypos - 3 * Piece.dy[dir];
                        tarx = startX;
                        tary = startY;
                        tarf = dir;
                        if (await UseCardWithContext(GetCard("滚木"))) { ok = true; break; }
                    }
                }
                if (ok) break;
            }
        }

        // 装备宝具（上下文感知）
        await EquipWeaponsInAttack();

        // 试图奇兵突袭
        if (HasCard("奇兵突袭"))
        {
            if (onBoardList.OfType<Servant>().Count() > 0)
            {
                targets = GetAttackCoreTargets();
                foreach (var target in targets)
                {
                    Piece killer;
                    if (target is Master)
                        killer = onBoardList.OfType<Servant>().OrderByDescending(t => t.AT).First();
                    else killer = onBoardList.OfType<Servant>()
                        .FirstOrDefault(s => CanKillSingle(s, target));
                    if (killer != null)
                    {
                        tar = killer;
                        // 找目标周围的空格
                        var emptyTiles = new List<(int, int,int)>();
                        for (int i = 0; i < 6; i++)
                        {
                            int nx = target.xpos + Piece.dx[i];
                            int ny = target.ypos + Piece.dy[i];
                            var t = GameManager.Instance.GetTile(nx, ny);
                            if (t != null && t.onTile == null)
                                emptyTiles.Add((nx, ny,i));
                        }
                        if (emptyTiles.Count > 0)
                        {
                            (tarx, tary,tarf) = emptyTiles[0];
                            await UseCardWithContext(GetCard("奇兵突袭"));
                        }
                    }
                }
            }
        }

        // 火球（攻击行为）
        if (HasCard("火球"))
        {
            await UseCardWithContext(GetCard("火球"));
        }

        // 滚木（攻击行为）
        if (HasCard("滚木"))
        {
            await UseCardWithContext(GetCard("滚木"));
        }
        isExecutingAttackBehavior = false;
    }

    private async UniTask ExecuteDefenseBehavior()
    {
        var targets = GetDefenseCoreTargets();

        // 飓风地形杀（防御行为）
        if (HasCard("飓风"))
        {
            bool ok = false;
            foreach (var target in targets)
            {
                for (int dir = 0; dir < 6; dir++)
                {
                    if (CanJuFengTerrainKill(target, dir))
                    {
                        int startX = target.xpos - 3 * Piece.dx[dir];
                        int startY = target.ypos - 3 * Piece.dy[dir];
                        tarx = startX;
                        tary = startY;
                        tarf = dir;
                        if (await UseCardWithContext(GetCard("飓风"))) { ok = true; break; }
                    }
                }
                if (ok) break;
            }
        }
        if (HasCard("滚木"))
        {
            bool ok = false;
            foreach (var target in targets)
            {
                for (int dir = 0; dir < 6; dir++)
                {
                    if (CanGunMuTerrainKill(target, dir))
                    {
                        int startX = target.xpos - 3 * Piece.dx[dir];
                        int startY = target.ypos - 3 * Piece.dy[dir];
                        tarx = startX;
                        tary = startY;
                        tarf = dir;
                        if (await UseCardWithContext(GetCard("滚木"))) { ok = true; break; }
                    }
                }
                if (ok) break;
            }
        }

        // 装备理想乡
        if (HasCard("遥远的理想乡"))
        {
            await UseCardWithContext(GetCard("遥远的理想乡"));
        }

        // 火球（防御行为）
        if (HasCard("火球"))
        {
            await UseCardWithContext(GetCard("火球"));
        }

        // 滚木（防御行为）
        if (HasCard("滚木"))
        {
            await UseCardWithContext(GetCard("滚木"));
        }

        if (HasCard("禁锢"))
        {
            await UseCardWithContext(GetCard("禁锢"));
        }
    }

    private async UniTask EquipWeaponsInAttack()
    {
        foreach (var s in onBoardList.OfType<Servant>())
        {
            string bestGear = null;
            if (s is Saber)
            {
                // 若将攻击 Master/傀儡，优先咖喱棒
                if (CanAttackMasterOrGolem(s))
                    bestGear = HasCard("誓约胜利之剑") ? "誓约胜利之剑" : null;
                else
                    bestGear = HasCard("开拓者的球棒") ? "开拓者的球棒" : null;
            }
            else if (s is Archer)
            {
                bestGear = HasCard("无限剑制") ? "无限剑制" : (HasCard("裂空之箭") ? "裂空之箭" : null);
            }
            else if (s is Lancer)
            {
                bestGear = HasCard("穿刺死棘之枪") ? "穿刺死棘之枪" : (HasCard("溯时之枪") ? "溯时之枪" : null);
            }
            else if (s is Caster)
            {
                bestGear = HasCard("破除万法之符") ? "破除万法之符" : null;
                if (bestGear == null && HasCard("开拓者的球棒")) bestGear = "开拓者的球棒";
                if (bestGear == null && HasCard("溯时之枪")) bestGear = "溯时之枪";   
            }
            else if (s is Berserker)
            {
                bestGear = HasCard("十二试炼") ? "十二试炼" : (HasCard("筑城者的骑枪") ? "筑城者的骑枪" : null);
            }
            else if (s is Assassin)
            {
                bestGear = HasCard("诅咒之手") ? "诅咒之手" : (HasCard("筑城者的骑枪") ? "筑城者的骑枪" : null);
            }

            if (bestGear == null && HasCard("钟表匠的礼帽")) bestGear = "钟表匠的礼帽";
            if (bestGear == null && HasCard("著者的羽毛笔")) bestGear = "著者的羽毛笔";
            
            if (bestGear != null)
            {
                tar = s;
                await UseCardWithContext(GetCard(bestGear));
            }
        }
    }

    private bool CanAttackMasterOrGolem(Piece s)
    {
        var targets = GetAllEnemies().Where(p => p is Master || p is Golem).ToList();
        return targets.Any(t => HexDist(s.xpos, s.ypos, t.xpos, t.ypos) <= s.RA);
    }

    // ========================
    // 普通从者行动
    // ========================
    private async UniTask ExecuteNormalServantActions()
    {
        List<Piece> buf = new List<Piece>(onBoardList.Where(p => p.canAct));
        foreach (var piece in buf)
        {
            currentUsingCard = piece;
            await piece.TakeAction();
            currentUsingCard = null;
        }
    }

    private void ApplyDefenseMarks()
    {
        defenseMarked.Clear();
        foreach (var p in onBoardList)
        {
            if (p is Saber || p is Rider)
            {
                if (HasNonPlainMetaPosition(p)) defenseMarked.Add(p);
            }
            else if (p is Archer || p is Caster)
            {
                if (HexDist(p.xpos, p.ypos, master.xpos, master.ypos) <= 1)
                {
                    int defenseCount = onBoardList.Count(x =>
                        defenseMarked.Contains(x) && HexDist(x.xpos, x.ypos, master.xpos, master.ypos) <= 2);
                    if (defenseCount < 2) defenseMarked.Add(p);
                }
            }
        }
    }

    private bool HasNonPlainMetaPosition(Piece p)
    {
        for (int i = 0; i < 6; i++)
        {
            Tile ti = GameManager.Instance.GetTile(p.xpos + dx[i], p.ypos + dy[i]);
            for(int j = 0; j < 6; j++)
            {
                if(j==(i+1)%6||i==(j+1)%6)continue;
                Tile tj = GameManager.Instance.GetTile(p.xpos + dx[j], p.ypos + dy[j]);
                if ((ti == null || ti.type != Terrain.Plain) && (tj == null || tj.type != Terrain.Plain))
                    return true;
            }
        }
        return false;
    }

    // ========================
    // 部署逻辑
    // ========================
    private async UniTask ExecuteDeploymentLogic()
    {
        // 傀儡
        if (HasCard("傀儡") && !onBoardList.Any(p => p is Golem))
        {
            await UseCardWithContext(GetCard("傀儡"));
        }
        else if (HasCard("滑翔机"))
        {
            await UseCardWithContext(GetCard("滑翔机"));
        }
        else if (HasCard("战车"))
        {
            await UseCardWithContext(GetCard("战车"));
        }

        // 召唤从者
        var bestServant = FindBestServantToSummon();
        if (bestServant != null)
        {
            await UseCardWithContext(bestServant);
        }
    }


    private async UniTask ExecuteUseSkillLogic()
    {
        if (HasCard("禁锢")) await UseCardWithContext(GetCard("禁锢"));
        if (HasCard("策反")) await UseCardWithContext(GetCard("策反"));
        if (HasCard("添加棋盘块")&&GameManager.Instance.tiles.Count<70) await UseCardWithContext(GetCard("添加棋盘块"));
        if (HasCard("移动棋盘块")) await UseCardWithContext(GetCard("移动棋盘块"));
        if (HasCard("地形修改")) await UseCardWithContext(GetCard("地形修改"));

        bool masterInDanger = GetAllEnemies().Any(e=>HexDist(e.xpos, e.ypos, master.xpos, master.ypos) <= e.RA);
        if (masterInDanger)
        {
            if (HasCard("转移阵地")) await UseCardWithContext(GetCard("转移阵地"));
        }
    }

    // ========================
    // 评分体系
    // ========================
    private Card FindBestServantToSummon()
    {
        var servants = hand.OfType<Servant>().ToList();
        if (!servants.Any()) return null;
        return servants.OrderByDescending(ScoreServantForSummon).First();
    }

    private int GetRealAT(Piece s)
    {
        int sAT = s.AT;
        if (s is Assassin) sAT += 1;
        if (s is Caster) sAT = s.AT * 2;
        return sAT;
    }

    private float ScoreServantForSummon(Servant s)
    {
        float score = s.HP + GetRealAT(s) * 1.25f + s.DF * 1.2f;

        score += GetComboBonus(s);

        if (s.canBanMagic > 0)
            score += 3 * GetAllEnemies().OfType<Caster>().Count();

        int waterCount = GameManager.Instance.tiles.Values.Count(t => t.type == Terrain.Water);
        int hillCount = GameManager.Instance.tiles.Values.Count(t => t.type == Terrain.Hill);
        if (s.canSwim > 0) score += 0.5f * waterCount;
        if (s.canClimb > 0) score += 0.5f * hillCount;

        if (s.canRide > 0) score += 5 * (onBoardList.OfType<Vehicle>().Count() + hand.OfType<Vehicle>().Count());

        return score;
    }

    private int GetComboBonus(Servant s)
    {
        int score = 0;
        
        if(s is Saber)
            score = (HasCard("遥远的理想乡") ? 10 : 0) + (HasCard("誓约胜利之剑") ? 10 : 0);
        if (s is Archer)
            score = Math.Max((HasCard("无限剑制") ? 10 : 0), (HasCard("裂空之箭") ? 8 : 0));
        if (s is Lancer)
            score = Math.Max((HasCard("穿刺死棘之枪") ? 10 : 0), (HasCard("溯时之枪") ? 8 : 0));
        if (s is Caster)
            score = (HasCard("破除万法之符") ? 10 : 0);
        if (s is Rider)
            score = (HasCard("鲜血神殿") ? 10 : 0);
        if (s is Assassin)
            score = (HasCard("诅咒之手") ? 10 : 0);
        if (s is Berserker)
            score = (HasCard("十二试炼") ? 10 : 0);

        if (GetRealAT(s) >= 4)
        {
            score = Math.Max(score, (HasCard("溯时之枪") ? 10 : 0));
            score = Math.Max(score, (HasCard("开拓者的球棒") ? 8 : 0));
            score = Math.Max(score, (HasCard("裂空之箭") ? 6 : 0));
        }
        if (s.DF < 2)
        {
            score = Math.Max(score, (HasCard("筑城者的骑枪") ? 8 : 0));
        }
        score = Math.Max(score, (HasCard("钟表匠的礼帽") ? 5 : 0));
        score = Math.Max(score, (HasCard("著者的羽毛笔") ? 5 : 0));

        return score;
    }

    private float ScoreGear(string name) => name switch
    {
        "遥远的理想乡" => 15,

        "无限剑制" => 13,
        "鲜血神殿" => 13,

        "破除万法之符" => 12,
        "誓约胜利之剑" => 12,
        "裂空之箭" => 12,
        "溯时之枪" => 12,

        "十二试炼" => 10,
        "诅咒之手" => 10,
        "穿刺死棘之枪" => 10,

        "筑城者的骑枪" => 8,
        "开拓者的球棒" => 8,
        "著者的羽毛笔" => 8,
        "钟表匠的礼帽" => 8,
        _ => 0
    };


    
    // ========================
    // 地形修改
    // ========================
    public override async UniTask EditTileList(List<Tile> tiles)
    {
        if(tiles.Count>1)return;
        Tile t = tiles[0];
        if (tarf != -1) t.type = (Terrain)tarf;
        else t.type = Terrain.Plain;
    }

    // ========================
    // 选择方法
    // ========================
    public override async UniTask<(int, int)> SelectPosition(List<(int, int)> validPositions, bool canCancel=false)
    {
        try
        {
            if (validPositions == null || !validPositions.Any()) return (-999, -999);
            if (tarx != -999 && tary != -999 && validPositions.Contains((tarx, tary)))
            {
                int x = tarx, y = tary;
                tarx = -999; tary = -999;
                return (x, y);
            }

            if (currentUsingCard == null) return SelectPositionMaster(validPositions);
            if (currentUsingCard is Servant s) return SelectPositionServant(s, validPositions);
            if (currentUsingCard is HuoQiu) return SelectPositionHuoQiu(validPositions);
            if (currentUsingCard is GunMu) return SelectPositionGunMu(validPositions);
            if (currentUsingCard is JuFeng) return SelectPositionJuFeng(validPositions);
            if (currentUsingCard is Golem) return SelectPositionGolem(validPositions);
            if (currentUsingCard is Glider) return SelectPositionGlider(validPositions);
            if (currentUsingCard is Truck) return SelectPositionTruck(validPositions);
            if (currentUsingCard is TianJia) return SelectPositionTianJia(validPositions);
            if (currentUsingCard is YiDong) return SelectPositionYiDong(validPositions);
            if (currentUsingCard is XiuGai) return SelectPositionXiuGai(validPositions);
            if (currentUsingCard is ZhuanYi)
            {
                return SelectPositionServant(master, validPositions);
            }

            return validPositions[0];
        }
        finally
        {
            await UniTask.Delay(500);
        }
        
    }

    public override async UniTask<int> SelectDirection(int x, int y, bool rot = false)
    {
        if (tarf != -1)
        {
            int f = tarf;
            tarf = -1;
            return f;
        }
        int bestDir = 0, maxEnemies = -1;
        List<int> cnt = new List<int> { 0, 0, 0, 0, 0, 0, 0 };
        for (int dir = 0; dir < 6; dir++)
        {
            cnt[dir] = 0;
            for (int r = 1; r <= 3; r++)
            {
                var tile = GameManager.Instance.GetTile(x + r * Piece.dx[dir], y + r * Piece.dy[dir]);
                if (tile?.onTile != null && tile.onTile.player.team != this.team) ++cnt[dir];
            }
        }
        cnt[6] = cnt[0];
        if (currentUsingCard is Piece p)
        {
            for (int dir = 0; dir < 6; dir++)
                if (cnt[dir] + cnt[dir + 1] > maxEnemies)
                {
                    maxEnemies = cnt[dir] + cnt[dir + 1];
                    bestDir = dir;
                }
            return bestDir;
        }
        else if (currentUsingCard is GunMu || currentUsingCard is JuFeng)
        {
            for (int dir = 0; dir < 6; dir++)
                if (cnt[dir] > maxEnemies)
                {
                    maxEnemies = cnt[dir];
                    bestDir = dir;
                }
            return bestDir;
        }
        else return 0;
    }

    public override async UniTask<Piece> SelectTarget(List<Piece> validTargets)
    {
        try
        {
            if (validTargets == null || validTargets.Count == 0) return null;
            if (tar != null && validTargets.Contains(tar))
            {
                Piece p = tar;
                tar = null;
                return p;
            }

            if (currentUsingCard is Piece ps)
            {
                validTargets = validTargets.Where(t => t.player.team != this.team).ToList();
                var killable = validTargets.Where(t => CanKillSingle(ps, t)).ToList();
                if (killable.Any())
                    return killable.OrderByDescending(t => t.AT).ThenBy(t => t.HP).First();
                return validTargets.OrderBy(t => t.HP).FirstOrDefault();
            }
            if (currentUsingCard is Weapon eq)
            {
                bool ok = true;
                if (eq.cardName == "遥远的理想乡")
                {
                    foreach (Piece p in validTargets)
                        if (p is Saber s && s.equip != null && s.equip.cardName == "誓约胜利之剑")
                        { ok = false; return p; }
                    if (ok) return validTargets.OfType<Saber>().OrderByDescending(t => t.HP).FirstOrDefault();
                }
                if (eq.cardName == "誓约胜利之剑")
                {
                    foreach (Piece p in validTargets)
                        if (p is Saber s && s.equip != null && s.equip.cardName == "遥远的理想乡")
                        { ok = false; return p; }
                    if (ok) return validTargets.OfType<Saber>().OrderByDescending(t => t.HP).FirstOrDefault();
                }
                if (eq.cardName == "裂空之箭")
                {
                    foreach (Piece p in validTargets)
                        if (p is Lancer s && s.equip != null && s.equip.cardName == "溯时之枪")
                        { ok = false; return validTargets.OfType<Archer>().OrderBy(t => t.HP).FirstOrDefault(); }
                    if (ok) return validTargets.OrderByDescending(t => GetRealAT(t)).FirstOrDefault();
                }
                if (eq.cardName == "溯时之枪")
                {
                    foreach (Piece p in validTargets)
                        if (p is Archer s && s.equip != null && s.equip.cardName == "裂空之箭")
                        { ok = false; return validTargets.OfType<Lancer>().OrderBy(t => t.HP).FirstOrDefault(); }
                    if (ok) return validTargets.OrderByDescending(t => GetRealAT(t)).FirstOrDefault();
                }
                if (eq.cardName == "无限剑制") return validTargets.OfType<Archer>().OrderByDescending(t => t.HP).FirstOrDefault();
                if (eq.cardName == "鲜血神殿") return validTargets.OfType<Rider>().OrderByDescending(t => t.HP).FirstOrDefault();
                if (eq.cardName == "破除万法之符") return validTargets.OfType<Caster>().OrderByDescending(t => t.HP).FirstOrDefault();
                if (eq.cardName == "十二试炼") return validTargets.OfType<Berserker>().OrderByDescending(t => t.HP).FirstOrDefault();
                if (eq.cardName == "诅咒之手") return validTargets.OfType<Assassin>().OrderByDescending(t => t.HP).FirstOrDefault();
                if (eq.cardName == "穿刺死棘之枪") return validTargets.OfType<Lancer>().OrderByDescending(t => t.HP).FirstOrDefault();

                if (eq.cardName == "筑城者的骑枪") return validTargets.OrderBy(t => t.DF).FirstOrDefault();
                else return validTargets.OrderByDescending(t => GetRealAT(t)).FirstOrDefault();
            }
            var tar2 = validTargets.OrderByDescending(t => GetRealAT(t)).FirstOrDefault();
            if (currentUsingCard is CeFan) currentUsingCard = tar2;
            return tar2;
        }
        finally
        {
            await UniTask.Delay(500);
        }
    }

    // ========================
    // 位置评分
    // ========================
    private (int, int) SelectPositionMaster(List<(int, int)> valid)
    {
        var (x, y) = valid.OrderByDescending(pos =>
        {
            int score = HexDist(pos.Item1, pos.Item2, 0, 0) * HexDist(pos.Item1, pos.Item2, 0, 0) - 1 + UnityEngine.Random.Range(0, 13);
            int threat = 0;
            foreach (var e in GetAllEnemies())
            {
                if (HexDist(e.xpos, e.ypos, pos.Item1, pos.Item2) <= e.RA) threat++;
            }
            score -= threat*10;
            return score;
        }).First();
        if (x < 0 && y < 0) tarf = 0;
        if (x >= 0 && y < 0 && x < (-y)) tarf = 1;
        if (x >= 0 && y < 0 && x >= (-y)) tarf = 2;
        if (x >= 0 && y >= 0) tarf = 3;
        if (x < 0 && y >= 0 && x >= (-y)) tarf = 4;
        if (x < 0 && y >= 0 && x < (-y)) tarf = 5;
        return (x, y);
    }

    private (int, int) SelectPositionServant(Piece s, List<(int, int)> valid)
    {
        return valid.OrderByDescending(pos => ScoreServantPosition(s, pos)).First();
    }

    private (int, int) SelectPositionHuoQiu(List<(int, int)> valid)
    {
        // 攻击目标：阿尔托莉雅、理想乡Saber、Master
        var attackTargets = GetAllEnemies().Where(t =>
            t is Arthuria ||
            (t is Saber s && s.equip?.cardName == "遥远的理想乡") ||
            t is Master
        ).ToList();

        // 防御目标：滑翔机、3/2人车、包涵、日落、Master
        var defenseTargets = GetAllEnemies().Where(t =>
            t is Glider ||
            (t is LoadAble l && l.onLoad.Count >= 2) ||
            t is Include ||
            t is Sunsettia ||
            t is Master
        ).ToList();

        return valid.OrderByDescending(pos =>
        {
            bool inAttack = isExecutingAttackBehavior;
            var targets = inAttack ? attackTargets : defenseTargets;
            return ScoreAreaSkillPosition(pos, targets, 1);
        }).First();
    }

    private (int, int) SelectPositionGunMu(List<(int, int)> valid)
    {
        var attackTargets = GetAllEnemies().Where(t =>
            t is Arthuria ||
            (t is Saber s && s.equip?.cardName == "遥远的理想乡") ||
            t is Master
        ).ToList();

        var defenseTargets = GetAllEnemies().Where(t =>
            t is Glider ||
            (t is LoadAble l && l.onLoad.Count >= 2) ||
            t is Include ||
            t is Sunsettia ||
            t is Master
        ).ToList();


        return valid.OrderByDescending(pos =>
        {
            bool inAttack = isExecutingAttackBehavior;
            var targets = inAttack ? attackTargets : defenseTargets;
            return ScoreLineSkillPosition(pos, targets, 4); // 滚木是直线4格
        }).First();
    }

    private (int, int) SelectPositionJuFeng(List<(int, int)> valid)
    {
        
        // 攻击目标：同火球
        var attackTargets = GetAllEnemies().Where(t =>
            t is Arthuria ||
            (t is Saber s && s.equip?.cardName == "遥远的理想乡") ||
            t is Master
        ).ToList();

        // 防御目标：3/2人车、包涵、日落、Master（无滑翔机！）
        var defenseTargets = GetAllEnemies().Where(t =>
            (t is LoadAble l && l.onLoad.Count >= 2) ||
            t is Include ||
            t is Sunsettia ||
            t is Master
        ).ToList();


        return valid.OrderByDescending(pos =>
        {
            bool inAttack = isExecutingAttackBehavior;
            var targets = inAttack ? attackTargets : defenseTargets;
            return ScoreLineSkillPosition(pos, targets, 4); // 同滚木
        }).First();
    }

    private (int, int) SelectPositionGolem(List<(int, int)> valid)
    {
        return valid.OrderByDescending(pos => ScoreGolemPosition(pos)).First();
    }

    private (int, int) SelectPositionGlider(List<(int, int)> valid)
    {
        if (currentUsingCard.status == CardStatus.InHand)
            return valid.OrderByDescending(pos => ScoreGliderPosition(pos)).First();
        else
            return valid.OrderByDescending(pos => {
                var (x, y) = pos;
                Tile t = GameManager.Instance.GetTile(x, y);
                if (t.onTile == null) return 0;
                if (t.onTile.player.team == this.team) return 0;
                int res = t.onTile.HP;
                if (t.onTile is LoadAble ve)
                    foreach (Piece p in ve.onLoad.OfType<Piece>())
                        res += p.HP;
                return res;
            }).First();
    }

    private (int, int) SelectPositionTruck(List<(int, int)> valid)
    {
        return valid.OrderByDescending(pos => ScoreTruckPosition(pos)).First();
    }

    private (int, int) SelectPositionTianJia(List<(int, int)> valid)
    {
        if (DecidePrimaryStrategy())
            return valid.OrderBy(pos => HexDist(pos.Item1,pos.Item2, master.xpos, master.ypos)).First();
        else
        {
            foreach(Player p in GameManager.Instance.players)
                if(p.team!=this.team)
                    return valid.OrderBy(pos => HexDist(pos.Item1,pos.Item2, p.master.xpos, p.master.ypos)).First();
        }
        return (9999, 9999);
    }

    private (int, int)? GetBlockCenter(int x, int y)
    {
        foreach (var tile in GameManager.Instance.tiles.Values)
        {
            if (tile.isCenter && HexDist(x, y, tile.xpos, tile.ypos) <= 1)
                return (tile.xpos, tile.ypos);

        }
        return null;
    }

    private (int, int) SelectPositionYiDong(List<(int, int)> valid)
    {
        if (GameManager.Instance.GetTile(valid[0].Item1, valid[0].Item2) != null)
        {
            float bestScore = 0f;
            (int, int) bestPos = valid[0];
            foreach (var pos in valid)
            {
                if (!GameManager.Instance.tiles.ContainsKey(pos)) continue;
                var blockCenter = GetBlockCenter(pos.Item1, pos.Item2);
                if (!blockCenter.HasValue) continue;

                float score = 0;
                for (int i = 0; i < 7; i++)
                {
                    int bx = blockCenter.Value.Item1 + Piece.dx[i];
                    int by = blockCenter.Value.Item2 + Piece.dy[i];
                    var t = GameManager.Instance.GetTile(bx, by);
                    if (t?.onTile != null)
                    {
                        if (t.onTile.player.team == this.team)
                        {
                            score += t.onTile.HP + t.onTile.AT;
                        }
                        else
                        {
                            score -= t.onTile.HP + t.onTile.AT;
                        }
                    }
                }

                if (Math.Abs(score) > Math.Abs(bestScore)) { bestScore = score; bestPos = pos; }
            }
            if (bestScore < 0) tarx = tary = 9999;
            else tarx = tary = 0;
            tarf = 0;
            //Debug.Log($"{tarx}, {tary}");
            return bestPos;
        }
        else
        {
            tarf = 0;
            if (tarx == 9999) return (9999, 9999);
            return SelectPositionTianJia(valid);
        }
    }

    private (int, int) SelectPositionXiuGai(List<(int, int)> valid)
    {
        var bestScore = -999f;
        (int, int) bestPos = valid[0];
        Terrain bestTar = Terrain.Plain;
        foreach (var pos in valid)
        {
            Tile tile = GameManager.Instance.GetTile(pos.Item1, pos.Item2);
            if (tile == null) continue;

            // 间位/对位非平原 → 改水域/山地
            if (tile.type == Terrain.Plain)
            {
                bool hasNonPlainMeta = false;
                for (int i = 0; i < 6; i++)
                {
                    Tile ti = GameManager.Instance.GetTile(pos.Item1 + dx[i], pos.Item2 + dy[i]);
                    for (int j = 0; j < 6; j++)
                    {
                        if (j == (i + 1) % 6 || i == (j + 1) % 6) continue;
                        Tile tj = GameManager.Instance.GetTile(pos.Item1 + dx[j], pos.Item2 + dy[j]);
                        if ((ti == null || ti.type != Terrain.Plain) && (tj == null || tj.type != Terrain.Plain))
                            hasNonPlainMeta = true;
                    }
                }

                if (hasNonPlainMeta)
                {
                    int noSwim = GameManager.Instance.players.Where(p => p.team != this.team).Sum(p => p.onBoardList.Count(x => x.canSwim == 0));
                    if (noSwim > bestScore) { bestScore = noSwim; bestPos = pos; bestTar = Terrain.Water; }
                    int noClimb = GameManager.Instance.players.Where(p => p.team != this.team).Sum(p => p.onBoardList.Count(x => x.canClimb == 0));
                    if (noClimb > bestScore) { bestScore = noClimb; bestPos = pos; bestTar = Terrain.Hill; }
                }
            }

            if (HexDist(pos.Item1, pos.Item2, master.xpos, master.ypos) <= 1)
            {
                // 己方 Master 旁无山 → 改山
                bool hasHillNearMaster = false;
                for (int i = 0; i < 6; i++)
                {
                    var t = GameManager.Instance.GetTile(master.xpos + Piece.dx[i], master.ypos + Piece.dy[i]);
                    if (t?.type == Terrain.Hill) { hasHillNearMaster = true; break; }
                }
                if (!hasHillNearMaster)
                {
                    float score = 5 + (HasCard("滑翔机") ? 5 : 0);
                    if (score > bestScore) { bestScore = score; bestPos = pos; bestTar = Terrain.Hill; }
                }
            }
            else
            {
                // 敌方 Master 周围逻辑
                var enemyMaster = GetAllEnemies().OfType<Master>().FirstOrDefault();
                if (enemyMaster != null)
                {
                    if (HexDist(pos.Item1, pos.Item2, enemyMaster.xpos, enemyMaster.ypos) <= 1)
                    {
                        int hillCount = 0;
                        for (int i = 0; i < 6; i++)
                        {
                            var t = GameManager.Instance.GetTile(enemyMaster.xpos + Piece.dx[i], enemyMaster.ypos + Piece.dy[i]);
                            if (t?.type == Terrain.Hill) hillCount++;
                        }
                        if (hillCount == 1 && tile.type == Terrain.Hill)
                        {
                            int noSwim = GameManager.Instance.players.Where(p => p.team != this.team).Sum(p => p.onBoardList.Count(x => x.canSwim == 0));
                            float score = 5 + noSwim;
                            if (score > bestScore) { bestScore = score; bestPos = pos; bestTar = Terrain.Water; }
                        }
                    }
                    else if (HexDist(pos.Item1, pos.Item2, enemyMaster.xpos, enemyMaster.ypos) <= 3)
                    {
                        if (tile.type == Terrain.Hill)
                        {
                            int myNoClimb = onBoardList.Count(x => x.canClimb == 0);
                            int enemyNoClimb = GameManager.Instance.players.Where(p => p.team != this.team).Sum(p => p.onBoardList.Count(x => x.canClimb == 0));
                            float score = myNoClimb - enemyNoClimb;
                            if (score > bestScore) { bestScore = score; bestPos = pos; bestTar = Terrain.Hill; }
                        }
                        else if (tile.type == Terrain.Water)
                        {
                            int myNoSwim = onBoardList.Count(x => x.canSwim == 0);
                            int enemyNoSwim = GameManager.Instance.players.Where(p => p.team != this.team).Sum(p => p.onBoardList.Count(x => x.canSwim == 0));
                            float score = myNoSwim - enemyNoSwim;
                            if (score > bestScore) { bestScore = score; bestPos = pos; bestTar = Terrain.Water; }
                        }
                    }
                }
            }
        }
        tarf = (int) bestTar;
        return bestPos;
    }

    // ========================
    // 评分函数
    // ========================
    private float ScoreServantPosition(Piece s, (int, int) pos)
    {
        float score = 0;
        int x = pos.Item1, y = pos.Item2;

        // 敌方核心单位距离
        var coreEnemies = GetAttackCoreTargets();
        if (coreEnemies.Any())
        {
            int minDist = coreEnemies.Min(e => HexDist(x, y, e.xpos, e.ypos));
            score += Mathf.Max(0, 8 - minDist)*10;
        }

        // 攻击 Master/傀儡
        var masters = GetAllEnemies().OfType<Master>();
        if (masters.Any(m => HexDist(x, y, m.xpos, m.ypos) <= s.RA))
        {
            score += 30;
        }

        // 驾驶
        Tile tile = GameManager.Instance.GetTile(x, y);
        if (tile != null)
        {
            if (tile.onTile is Vehicle && s.canRide > 0) score += 100;
            else
            {
                bool canReachVehicle = onBoardList.OfType<Vehicle>().Any(v => HexDist(x, y, v.xpos, v.ypos) <= s.ST);
                if (canReachVehicle) score += 50;
            }
        }

        // 地形
        if (tile != null)
        {
            if (tile.type == Terrain.Hill)
            {
                score += s.canClimb > 0 ? 30 : -10;
            }
            else if (tile.type == Terrain.Water)
            {
                score += s.canSwim > 0 ? 30 : -999;
            }
        }

        // 敌人威胁
        int threat = 0;
        foreach (var e in GetAllEnemies())
        {
            if (HexDist(e.xpos, e.ypos, x, y) <= e.RA) threat++;
        }
        score -= threat*5;
        if (s is Master) score -= threat*15;

        return score + UnityEngine.Random.Range(0, 10);
    }

    private float ScoreAreaSkillPosition((int, int) pos, List<Piece> highValueTargets, int radius)
    {
        float score = 0;
        var affected = new HashSet<(int, int)>();
        affected.Add(pos);
        for (int r = 1; r <= radius; r++)
        {
            for (int i = 0; i < 6; i++)
            {
                affected.Add((pos.Item1 + r * Piece.dx[i], pos.Item2 + r * Piece.dy[i]));
            }
        }

        foreach (var p in affected)
        {
            var tile = GameManager.Instance.GetTile(p.Item1, p.Item2);
            if (tile?.onTile != null && tile.onTile.player.team != this.team)
            {
                var target = tile.onTile;
                if (highValueTargets.Contains(target)) score += 10;
                else score += 1;
            }
        }
        return score;
    }

    private float ScoreLineSkillPosition((int, int) startPos, List<Piece> highValueTargets, int length)
    {
        float bestScore = -999;
        for (int dir = 0; dir < 6; dir++)
        {
            float score = 0;
            for (int i = 0; i < length; i++)
            {
                int x = startPos.Item1 + i * Piece.dx[dir];
                int y = startPos.Item2 + i * Piece.dy[dir];
                var tile = GameManager.Instance.GetTile(x, y);
                if (tile?.onTile != null && tile.onTile.player.team != this.team)
                {
                    var target = tile.onTile;
                    if (highValueTargets.Contains(target)) score += 10;
                    else score += 1;
                }
            }
            if (score > bestScore) bestScore = score; 
        }
        return bestScore;
    }

    private float ScoreGolemPosition((int, int) pos)
    {
        int x = pos.Item1, y = pos.Item2;
        if (HexDist(x, y, master.xpos, master.ypos) < 2) return -999;

        float score = 0;

        // 敌人威胁
        int threat = 0;
        foreach (var e in GetAllEnemies())
        {
            if (HexDist(e.xpos, e.ypos, x, y) <= e.RA) threat++;
        }
        score -= threat;

        // Master 危险
        bool masterInDanger = GetAllEnemies().Any(e=>HexDist(e.xpos, e.ypos, master.xpos, master.ypos) <= e.RA);
        if (masterInDanger)
        {
            int distToMaster = HexDist(x, y, master.xpos, master.ypos);
            score += Mathf.Max(8, distToMaster);
        }

        // 地形适配
        int waterAround = 0, hillAround = 0;
        for (int i = 0; i < 6; i++)
        {
            var t = GameManager.Instance.GetTile(x + Piece.dx[i], y + Piece.dy[i]);
            if (t?.type == Terrain.Water) waterAround++;
            if (t?.type == Terrain.Hill) hillAround++;
        }
        int swimCount = hand.Count(c => c is Piece s && s.canSwim > 0);
        int climbCount = hand.Count(c => c is Piece s && s.canClimb > 0);
        if (waterAround > swimCount) score -= 2;
        if (hillAround > climbCount) score -= 1;

        // 攻击敌方 Master
        var enemyMasters = GetAllEnemies().OfType<Master>();
        if (enemyMasters.Any(em => HexDist(x, y, em.xpos, em.ypos) <= 2))
        {
            int maxAt = 0;
            List<Servant> _buf = hand.OfType<Servant>().ToList();
            if (_buf.Count > 0) maxAt = _buf.Max(s => s.AT);
            score += 2 + maxAt;
        }

        return score;
    }

    private float ScoreGliderPosition((int, int) pos)
    {
        Tile tile = GameManager.Instance.GetTile(pos.Item1, pos.Item2);
        if (tile == null || tile.type != Terrain.Hill) return -999;

        float score = 6f;

        // 敌人威胁
        int threat = 0;
        foreach (var e in GetAllEnemies())
        {
            if (HexDist(e.xpos, e.ypos, pos.Item1, pos.Item2) <= e.RA) threat++;
        }
        score -= 2 * threat;

        // 地形适配
        int waterAround = 0, hillAround = 0;
        for (int i = 0; i < 6; i++)
        {
            var t = GameManager.Instance.GetTile(pos.Item1 + Piece.dx[i], pos.Item2 + Piece.dy[i]);
            if (t?.type == Terrain.Water) waterAround++;
            if (t?.type == Terrain.Hill) hillAround++;
        }
        int swimCount = hand.Count(c => c is Servant s && s.canSwim > 0);
        int climbCount = hand.Count(c => c is Servant s && s.canClimb > 0);
        if (waterAround > swimCount) score -= 2;
        if (hillAround > climbCount) score -= 1;

        return score;
    }

    private float ScoreTruckPosition((int, int) pos)
    {

        var enemyMaster = GetAllEnemies().OfType<Master>().FirstOrDefault();
        if (enemyMaster == null) return 0;

        int dist = HexDist(pos.Item1, pos.Item2, enemyMaster.xpos, enemyMaster.ypos);
        float score = Mathf.Max(0, 8 - dist);

        Tile tile = GameManager.Instance.GetTile(pos.Item1, pos.Item2);
        if (tile?.type == Terrain.Hill) score -= 0.5f;

        score -= (HexDist(pos.Item1, pos.Item2, master.xpos, master.ypos) - 1) * 0.5f;

        return score;
    }


    private bool CanKillSingle(Piece attacker, Piece target)
    {
        int dmg = attacker.AT;
        if (target is Saber s && s.equip?.cardName == "遥远的理想乡") dmg = Mathf.Max(0, dmg - 1);
        return dmg > target.DF && (dmg - target.DF) >= target.HP;
    }
    // ========================
    // 辅助
    // ========================
    private int HexDist(int x1, int y1, int x2, int y2) => GameManager.HexDist(x1, y1, x2, y2);
    private bool DecidePrimaryStrategy()
    {
        float myPower = GameManager.Instance.players.Where(p => p.team == this.team).SelectMany(p => p.onBoardList.OfType<Piece>()).Sum(s => s.HP + s.AT * 1.25f + s.DF * 1.2f);
        float enemyPower = GameManager.Instance.players.Where(p => p.team != this.team).SelectMany(p => p.onBoardList.OfType<Piece>()).Sum(s => s.HP + s.AT * 1.25f + s.DF * 1.2f);
        float ratio = myPower / (enemyPower + 1e-6f);
        return ratio * attackBias >= defenseBias;
    }

    public override async UniTask<bool> useWuXie(Skill skill, Player usr, bool status)
    {
        if ((usr.team == this.team) == status) return false;
        int cnt = 0;
        for (int i = 0; i < 4; i++) if (hand[i] is WuXie) ++cnt;
        if (cnt == 0) return false;
        if (cnt >= 1)
        {
            if (skill is TuXi || skill is CeFan || skill is WuZhong || skill is GunMu || (skill is TianJia && GameManager.Instance.tiles.Count >= 70))
            {
                for (int i = 0; i < 4; i++)
                    if (hand[i] is WuXie)
                    {
                        GameObject go = GameObject.Instantiate(UIManager.Instance.UICardPrefab, UIManager.Instance.canvas);
                        UIRenderer rend = go.GetComponent<UIRenderer>();
                        rend.data = hand[i];rend.pos = 0;
                        rend.InitSprite();
                        rend.rect.anchoredPosition = new Vector2(-725, 750);
                        go.SetActive(true);
                        await UniTask.Delay(2000);
                        GameObject.Destroy(go);
                        await UniTask.Delay(500);

                        GameManager.Instance.DiscardCard(hand[i]);
                        hand[i] = null;
                        return true;
                    }
            }
        }
        if (cnt >= 2)
        {
            for (int i = 0; i < 4; i++)
                if (hand[i] is WuXie)
                {
                    GameObject go = GameObject.Instantiate(UIManager.Instance.UICardPrefab, UIManager.Instance.canvas);
                    UIRenderer rend = go.GetComponent<UIRenderer>();
                    rend.data = hand[i];rend.pos = 0;
                    rend.InitSprite();
                    rend.rect.anchoredPosition = new Vector2(-725, 750);
                    go.SetActive(true);
                    await UniTask.Delay(2000);
                    GameObject.Destroy(go);
                    await UniTask.Delay(500);
                        
                    GameManager.Instance.DiscardCard(hand[i]);
                    hand[i] = null;
                    return true;
                }
        }
        return false;
    }
    public override async UniTask UpdateHandCard()
    {
        for (int i = 0; i < 4; i++) if (hand[i] == null)
        {
            Card draw = GameManager.Instance.DrawCard();
            draw.player = this; hand[i] = draw;
        }
    }
    public override async UniTask InitUI()
    {
        
    }
}