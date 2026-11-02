using System;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace ReclayedCoop
{
    /// The mod's texts in every language the game has. They are added to the
    /// game's own LocalisationManager tables, so menu rows switch language and
    /// font exactly like the game's rows (clay letters, TMP for CJK / Thai).
    internal static class Loc
    {
        // Same column names as StreamingAssets/messages*.csv.
        private static readonly string[] Locales =
        {
            "en_gb", "de_de", "fr_fr", "it_it", "pt_pt", "pt_br", "es_es", "es_mx",
            "pl_pl", "ko_kr", "ru_ru", "ja_jp", "zh_cn", "zh_tw", "th_th", "id_id"
        };

        private static readonly string[][] Rows =
        {
            // ---- main menu and co-op screen
            new string[] { "Coop.Menu", "Online Co-op", "Online-Koop", "Coop en ligne", "Co-op online", "Cooperativo online", "Cooperativo online", "Cooperativo en línea", "Cooperativo en línea", "Co-op online", "온라인 협동", "Совместная игра", "オンライン協力", "在线合作", "線上合作", "เล่นร่วมกันออนไลน์", "Co-op Online" },
            new string[] { "Coop.Invite", "Invite a Friend", "Freund einladen", "Inviter un ami", "Invita un amico", "Convidar um amigo", "Convidar amigo", "Invitar a un amigo", "Invitar a un amigo", "Zaproś znajomego", "친구 초대", "Пригласить друга", "フレンドを招待", "邀请好友", "邀請好友", "เชิญเพื่อน", "Undang Teman" },
            new string[] { "Coop.Ready", "Ready", "Bereit", "Prêt", "Pronto", "Pronto", "Pronto", "Listo", "Listo", "Gotowe", "준비", "Готов", "準備OK", "准备", "準備", "พร้อม", "Siap" },
            new string[] { "Coop.Cancel", "Cancel", "Abbrechen", "Annuler", "Annulla", "Cancelar", "Cancelar", "Cancelar", "Cancelar", "Anuluj", "취소", "Отмена", "キャンセル", "取消", "取消", "ยกเลิก", "Batal" },
            new string[] { "Coop.Leave", "Leave", "Verlassen", "Quitter", "Esci", "Sair", "Sair", "Salir", "Salir", "Wyjdź", "나가기", "Выйти", "退出", "退出", "離開", "ออก", "Keluar" },
            new string[] { "Coop.HostIp", "Host by IP", "Per IP hosten", "Héberger par IP", "Ospita via IP", "Criar por IP", "Hospedar por IP", "Crear por IP", "Crear por IP", "Hostuj przez IP", "IP로 호스트", "Создать по IP", "IPでホスト", "通过IP创建", "透過IP建立", "สร้างผ่าน IP", "Host via IP" },
            new string[] { "Coop.JoinIp", "Join by IP", "Per IP beitreten", "Rejoindre par IP", "Unisciti via IP", "Entrar por IP", "Entrar por IP", "Unirse por IP", "Unirse por IP", "Dołącz przez IP", "IP로 참가", "Подключиться по IP", "IPで参加", "通过IP加入", "透過IP加入", "เข้าร่วมผ่าน IP", "Gabung via IP" },
            new string[] { "Coop.IpPrompt", "type host IP", "Host-IP eingeben", "entrez l'IP de l'hôte", "inserisci l'IP dell'host", "introduza o IP do anfitrião", "digite o IP do anfitrião", "escribe la IP del anfitrión", "escribe la IP del anfitrión", "wpisz IP hosta", "호스트 IP 입력", "введите IP хоста", "ホストのIPを入力", "输入主机IP", "輸入主機IP", "พิมพ์ IP ของโฮสต์", "ketik IP host" },
            new string[] { "Coop.Connected", "Connected", "Verbunden", "Connecté", "Connesso", "Ligado", "Conectado", "Conectado", "Conectado", "Połączono", "연결됨", "Подключено", "接続済み", "已连接", "已連線", "เชื่อมต่อแล้ว", "Terhubung" },
            new string[] { "Coop.InviteHint", "Invite a friend in Steam", "Lade einen Freund über Steam ein", "Invitez un ami sur Steam", "Invita un amico su Steam", "Convide um amigo no Steam", "Convide um amigo na Steam", "Invita a un amigo en Steam", "Invita a un amigo en Steam", "Zaproś znajomego na Steamie", "Steam에서 친구를 초대하세요", "Пригласите друга в Steam", "Steamでフレンドを招待してください", "在Steam中邀请好友", "在Steam中邀請好友", "เชิญเพื่อนผ่าน Steam", "Undang teman lewat Steam" },
            new string[] { "Coop.JoiningSteam", "Joining the game...", "Trete dem Spiel bei...", "Connexion à la partie...", "Accesso alla partita...", "A entrar no jogo...", "Entrando no jogo...", "Uniéndose a la partida...", "Uniéndose a la partida...", "Dołączanie do gry...", "게임에 참가하는 중...", "Подключение к игре...", "ゲームに参加中...", "正在加入游戏...", "正在加入遊戲...", "กำลังเข้าร่วมเกม...", "Bergabung ke permainan..." },
            new string[] { "Coop.WaitingPort", "Waiting for player 2 - port {0}", "Warte auf Spieler 2 - Port {0}", "En attente du joueur 2 - port {0}", "In attesa del giocatore 2 - porta {0}", "À espera do jogador 2 - porta {0}", "Aguardando o jogador 2 - porta {0}", "Esperando al jugador 2 - puerto {0}", "Esperando al jugador 2 - puerto {0}", "Czekam na gracza 2 - port {0}", "플레이어 2 대기 중 - 포트 {0}", "Ждём второго игрока - порт {0}", "プレイヤー2を待っています - ポート {0}", "等待玩家2 - 端口 {0}", "等待玩家2 - 連接埠 {0}", "รอผู้เล่น 2 - พอร์ต {0}", "Menunggu pemain 2 - port {0}" },
            new string[] { "Coop.Connecting", "Connecting...", "Verbinde...", "Connexion...", "Connessione...", "A ligar...", "Conectando...", "Conectando...", "Conectando...", "Łączenie...", "연결 중...", "Подключение...", "接続中...", "正在连接...", "連線中...", "กำลังเชื่อมต่อ...", "Menghubungkan..." },
            new string[] { "Coop.NoSteam", "Steam is not running", "Steam läuft nicht", "Steam n'est pas lancé", "Steam non è avviato", "O Steam não está aberto", "A Steam não está aberta", "Steam no se está ejecutando", "Steam no se está ejecutando", "Steam nie jest uruchomiony", "Steam이 실행 중이 아닙니다", "Steam не запущен", "Steamが起動していません", "Steam未运行", "Steam未執行", "Steam ไม่ได้เปิดอยู่", "Steam tidak berjalan" },
            new string[] { "Coop.NoOverlay", "Turn on the Steam overlay to invite", "Aktiviere das Steam-Overlay zum Einladen", "Activez l'overlay Steam pour inviter", "Attiva l'overlay di Steam per invitare", "Ative a sobreposição Steam para convidar", "Ative o overlay da Steam para convidar", "Activa la interfaz de Steam para invitar", "Activa el overlay de Steam para invitar", "Włącz nakładkę Steam, aby zaprosić", "초대하려면 Steam 오버레이를 켜세요", "Включите оверлей Steam, чтобы пригласить", "招待するにはSteamオーバーレイを有効にしてください", "请开启Steam界面以邀请", "請開啟Steam介面以邀請", "เปิดโอเวอร์เลย์ Steam เพื่อเชิญ", "Aktifkan overlay Steam untuk mengundang" },
            new string[] { "Coop.JoinFailed", "Could not join the game", "Beitritt fehlgeschlagen", "Impossible de rejoindre la partie", "Impossibile unirsi alla partita", "Não foi possível entrar no jogo", "Não foi possível entrar no jogo", "No se pudo unir a la partida", "No se pudo unir a la partida", "Nie udało się dołączyć do gry", "게임에 참가할 수 없습니다", "Не удалось подключиться к игре", "ゲームに参加できませんでした", "无法加入游戏", "無法加入遊戲", "ไม่สามารถเข้าร่วมเกมได้", "Gagal bergabung ke permainan" },
            new string[] { "Coop.Cancelled", "Cancelled", "Abgebrochen", "Annulé", "Annullato", "Cancelado", "Cancelado", "Cancelado", "Cancelado", "Anulowano", "취소됨", "Отменено", "キャンセルしました", "已取消", "已取消", "ยกเลิกแล้ว", "Dibatalkan" },

            // ---- messages and pause screen during a match
            new string[] { "Coop.Paused", "PAUSED", "PAUSIERT", "EN PAUSE", "IN PAUSA", "PAUSADO", "PAUSADO", "EN PAUSA", "EN PAUSA", "PAUZA", "일시 정지됨", "ПАУЗА", "ポーズ", "暂停", "暫停", "หยุดชั่วคราว", "JEDA" },
            new string[] { "Coop.WaitingP2", "Waiting for player 2...", "Warte auf Spieler 2...", "En attente du joueur 2...", "In attesa del giocatore 2...", "À espera do jogador 2...", "Aguardando o jogador 2...", "Esperando al jugador 2...", "Esperando al jugador 2...", "Czekam na gracza 2...", "플레이어 2를 기다리는 중...", "Ждём второго игрока...", "プレイヤー2を待っています...", "等待玩家2...", "等待玩家2...", "กำลังรอผู้เล่น 2...", "Menunggu pemain 2..." },
            new string[] { "Coop.Seconds", "{0} s", "{0} s", "{0} s", "{0} s", "{0} s", "{0} s", "{0} s", "{0} s", "{0} s", "{0}초", "{0} с", "{0}秒", "{0}秒", "{0}秒", "{0} วินาที", "{0} dtk" },
            new string[] { "Coop.ConnLost", "Connection lost", "Verbindung verloren", "Connexion perdue", "Connessione persa", "Ligação perdida", "Conexão perdida", "Conexión perdida", "Conexión perdida", "Utracono połączenie", "연결 끊김", "Связь потеряна", "接続が切れました", "连接已断开", "連線中斷", "การเชื่อมต่อขาดหาย", "Koneksi terputus" },
            new string[] { "Coop.HostLeft", "The host left", "Der Host ist gegangen", "L'hôte est parti", "L'host è uscito", "O anfitrião saiu", "O anfitrião saiu", "El anfitrión se fue", "El anfitrión se fue", "Host wyszedł", "호스트가 나갔습니다", "Хост вышел", "ホストが退出しました", "主机已离开", "主機已離開", "โฮสต์ออกจากเกมแล้ว", "Host keluar" },
            new string[] { "Coop.P2Left", "Player 2 left", "Spieler 2 ist gegangen", "Le joueur 2 est parti", "Il giocatore 2 è uscito", "O jogador 2 saiu", "O jogador 2 saiu", "El jugador 2 se fue", "El jugador 2 se fue", "Gracz 2 wyszedł", "플레이어 2가 나갔습니다", "Второй игрок вышел", "プレイヤー2が退出しました", "玩家2已离开", "玩家2已離開", "ผู้เล่น 2 ออกจากเกมแล้ว", "Pemain 2 keluar" },
            new string[] { "Coop.YouLeft", "You left the co-op game", "Du hast das Koop-Spiel verlassen", "Vous avez quitté la partie coop", "Hai lasciato la partita co-op", "Saíste do jogo cooperativo", "Você saiu do jogo cooperativo", "Saliste de la partida cooperativa", "Saliste de la partida cooperativa", "Opuszczono grę co-op", "협동 게임에서 나갔습니다", "Вы вышли из совместной игры", "協力プレイから退出しました", "你已退出合作游戏", "你已退出合作遊戲", "คุณออกจากเกมร่วมกันแล้ว", "Kamu keluar dari permainan co-op" },
            new string[] { "Coop.EscAgain", "Press Esc again to leave", "Drücke erneut Esc zum Verlassen", "Appuyez à nouveau sur Échap pour quitter", "Premi di nuovo Esc per uscire", "Prime Esc novamente para sair", "Pressione Esc novamente para sair", "Pulsa Esc otra vez para salir", "Presiona Esc otra vez para salir", "Naciśnij Esc ponownie, aby wyjść", "나가려면 Esc를 다시 누르세요", "Нажмите Esc ещё раз, чтобы выйти", "もう一度Escを押すと退出します", "再按一次Esc退出", "再按一次Esc離開", "กด Esc อีกครั้งเพื่อออก", "Tekan Esc lagi untuk keluar" },
            new string[] { "Coop.LinkLost", "Connection to player 2 lost", "Verbindung zu Spieler 2 verloren", "Connexion avec le joueur 2 perdue", "Connessione con il giocatore 2 persa", "Ligação ao jogador 2 perdida", "Conexão com o jogador 2 perdida", "Se perdió la conexión con el jugador 2", "Se perdió la conexión con el jugador 2", "Utracono połączenie z graczem 2", "플레이어 2와의 연결이 끊겼습니다", "Связь со вторым игроком потеряна", "プレイヤー2との接続が切れました", "与玩家2的连接已断开", "與玩家2的連線中斷", "การเชื่อมต่อกับผู้เล่น 2 ขาดหาย", "Koneksi ke pemain 2 terputus" },
            new string[] { "Coop.P2NotBack", "Player 2 did not come back", "Spieler 2 ist nicht zurückgekehrt", "Le joueur 2 n'est pas revenu", "Il giocatore 2 non è tornato", "O jogador 2 não voltou", "O jogador 2 não voltou", "El jugador 2 no volvió", "El jugador 2 no volvió", "Gracz 2 nie wrócił", "플레이어 2가 돌아오지 않았습니다", "Второй игрок не вернулся", "プレイヤー2が戻りませんでした", "玩家2没有回来", "玩家2沒有回來", "ผู้เล่น 2 ไม่ได้กลับมา", "Pemain 2 tidak kembali" },
            new string[] { "Coop.CantContinue", "The match cannot continue", "Das Spiel kann nicht fortgesetzt werden", "La partie ne peut pas continuer", "La partita non può continuare", "O jogo não pode continuar", "A partida não pode continuar", "La partida no puede continuar", "La partida no puede continuar", "Nie można kontynuować meczu", "경기를 계속할 수 없습니다", "Матч нельзя продолжить", "試合を続けられません", "比赛无法继续", "比賽無法繼續", "ไม่สามารถเล่นต่อได้", "Pertandingan tidak bisa dilanjutkan" },
            new string[] { "Coop.WaitShip", "Waiting for player 2 to pick a ship...", "Warte, bis Spieler 2 ein Schiff wählt...", "Le joueur 2 choisit son vaisseau...", "Il giocatore 2 sta scegliendo la nave...", "À espera que o jogador 2 escolha a nave...", "Aguardando o jogador 2 escolher a nave...", "Esperando a que el jugador 2 elija nave...", "Esperando a que el jugador 2 elija nave...", "Czekam, aż gracz 2 wybierze statek...", "플레이어 2가 기체를 고르는 중...", "Ждём, пока второй игрок выберет корабль...", "プレイヤー2が機体を選んでいます...", "等待玩家2选择飞船...", "等待玩家2選擇飛船...", "รอผู้เล่น 2 เลือกยาน...", "Menunggu pemain 2 memilih pesawat..." },
            new string[] { "Coop.ShipReady", "Ship ready - waiting for the host...", "Schiff bereit - warte auf den Host...", "Vaisseau prêt - en attente de l'hôte...", "Nave pronta - in attesa dell'host...", "Nave pronta - à espera do anfitrião...", "Nave pronta - aguardando o anfitrião...", "Nave lista - esperando al anfitrión...", "Nave lista - esperando al anfitrión...", "Statek gotowy - czekam na hosta...", "기체 준비 완료 - 호스트를 기다리는 중...", "Корабль выбран. Ждём хоста...", "機体の準備完了 - ホストを待っています...", "飞船已就绪 - 等待主机...", "飛船已就緒 - 等待主機...", "ยานพร้อมแล้ว - รอโฮสต์...", "Pesawat siap - menunggu host..." },
            new string[] { "Coop.HostChoosing", "The host is choosing", "Der Host wählt", "L'hôte choisit", "L'host sta scegliendo", "O anfitrião está a escolher", "O anfitrião está escolhendo", "El anfitrión está eligiendo", "El anfitrión está eligiendo", "Host wybiera", "호스트가 선택하는 중", "Хост выбирает - ждите", "ホストが選択中です", "主机正在选择", "主機正在選擇", "โฮสต์กำลังเลือก", "Host sedang memilih" },
            new string[] { "Coop.OnlyHostBack", "Only the host can go back", "Nur der Host kann zurückgehen", "Seul l'hôte peut revenir en arrière", "Solo l'host può tornare indietro", "Só o anfitrião pode voltar", "Só o anfitrião pode voltar", "Solo el anfitrión puede volver", "Solo el anfitrión puede volver", "Tylko host może się cofnąć", "호스트만 뒤로 갈 수 있습니다", "Назад может вернуть только хост", "戻れるのはホストだけです", "只有主机可以返回", "只有主機可以返回", "มีเพียงโฮสต์ที่ย้อนกลับได้", "Hanya host yang bisa kembali" },
            new string[] { "Coop.P2Ready", "Player 2 is ready. Pick your ship", "Spieler 2 ist bereit. Wähle dein Schiff", "Le joueur 2 est prêt. Choisissez votre vaisseau", "Il giocatore 2 è pronto. Scegli la nave", "O jogador 2 está pronto. Escolhe a tua nave", "O jogador 2 está pronto. Escolha sua nave", "El jugador 2 está listo. Elige tu nave", "El jugador 2 está listo. Elige tu nave", "Gracz 2 jest gotowy. Wybierz statek", "플레이어 2 준비 완료. 기체를 고르세요", "Второй игрок готов. Выберите корабль", "プレイヤー2の準備完了。機体を選んでください", "玩家2已就绪。请选择飞船", "玩家2已就緒。請選擇飛船", "ผู้เล่น 2 พร้อมแล้ว เลือกยานของคุณ", "Pemain 2 siap. Pilih pesawatmu" },
            new string[] { "Coop.OutOfSync", "Games out of sync", "Spiele nicht synchron", "Parties désynchronisées", "Partite non sincronizzate", "Jogos dessincronizados", "Jogos dessincronizados", "Partidas desincronizadas", "Partidas desincronizadas", "Gry nie są zsynchronizowane", "게임 동기화 오류", "Игры рассинхронизировались", "同期がずれました", "游戏不同步", "遊戲不同步", "เกมไม่ซิงค์กัน", "Permainan tidak sinkron" },
            new string[] { "Coop.PartnerClosed", "Partner closed the game", "Der Partner hat das Spiel beendet", "Le partenaire a fermé le jeu", "Il compagno ha chiuso il gioco", "O parceiro fechou o jogo", "O parceiro fechou o jogo", "El compañero cerró el juego", "El compañero cerró el juego", "Partner zamknął grę", "상대가 게임을 종료했습니다", "Партнёр закрыл игру", "相手がゲームを終了しました", "队友关闭了游戏", "隊友關閉了遊戲", "คู่หูปิดเกมแล้ว", "Rekan menutup permainan" },
            new string[] { "Coop.YouLeftMatch", "You left the match", "Du hast das Spiel verlassen", "Vous avez quitté la partie", "Hai lasciato la partita", "Saíste da partida", "Você saiu da partida", "Saliste de la partida", "Saliste de la partida", "Opuszczono mecz", "경기에서 나갔습니다", "Вы вышли из матча", "試合から退出しました", "你已退出比赛", "你已退出比賽", "คุณออกจากการแข่งขันแล้ว", "Kamu keluar dari pertandingan" },
        };

        private static readonly FieldInfo bsUseLocalized = AccessTools.Field(typeof(global::ButtonScript), "useLocalizedText");
        private static readonly FieldInfo bsLocalizedText = AccessTools.Field(typeof(global::ButtonScript), "localizedText");
        private static readonly FieldInfo bsText = AccessTools.Field(typeof(global::ButtonScript), "text");
        private static readonly FieldInfo ptUseLocalized = AccessTools.Field(typeof(global::PText), "useLocalizedText");
        private static readonly FieldInfo ptLocalizedId = AccessTools.Field(typeof(global::PText), "localizedTextID");

        internal static void ApplyPatches(Harmony h)
        {
            // The game clears all tables when it (re)loads a game mode's texts.
            MethodInfo reset = AccessTools.Method(typeof(global::LocalisationManager), "ResetStrings");
            if (reset != null) h.Patch(reset, null, new HarmonyMethod(typeof(Loc), "Register"), null, null, null);
            Register();
        }

        private static void Register()
        {
            StringBuilder sb = new StringBuilder("Meta.Key");
            for (int i = 0; i < Locales.Length; i++) sb.Append(",\"").Append(Locales[i]).Append('"');
            for (int r = 0; r < Rows.Length; r++)
            {
                sb.Append('\n').Append(Rows[r][0]);
                for (int i = 1; i < Rows[r].Length; i++) sb.Append(",\"").Append(Rows[r][i].Replace("\"", "\"\"")).Append('"');
            }
            try { global::LocalisationManager.ApplyString(sb.ToString()); }
            catch (Exception e) { Plugin.Log.LogWarning("Could not add co-op texts to the game's localisation: " + e.Message); }
        }

        /// Text for the game's current language.
        internal static string Get(string key, params object[] args)
        {
            string s;
            if (global::LocalisationManager.TryGet(out s, key, args)) return s;
            Register();
            if (global::LocalisationManager.TryGet(out s, key, args)) return s;
            return English(key, args);
        }

        internal static string English(string key, params object[] args)
        {
            for (int r = 0; r < Rows.Length; r++)
                if (Rows[r][0] == key) return args.Length > 0 ? string.Format(Rows[r][1], args) : Rows[r][1];
            return key;
        }

        /// Make a cloned menu row show a localized key; the game then keeps it
        /// in the current language (and font) by itself.
        internal static void Bind(GameObject row, string key)
        {
            global::ButtonScript bs = row.GetComponent<global::ButtonScript>();
            global::PText pt = null;
            if (bs != null)
            {
                bsUseLocalized.SetValue(bs, true);
                bsLocalizedText.SetValue(bs, key);
                pt = bsText.GetValue(bs) as global::PText;
            }
            if (pt == null) pt = row.GetComponentInChildren<global::PText>(true);
            if (pt != null)
            {
                ptUseLocalized.SetValue(pt, true);
                ptLocalizedId.SetValue(pt, key);
                pt.UpdateText();
                return;
            }
            TMPro.TMP_Text tmp = row.GetComponentInChildren<TMPro.TMP_Text>(true);
            if (tmp != null) { tmp.text = Get(key); return; }
            UnityEngine.UI.Text t = row.GetComponentInChildren<UnityEngine.UI.Text>(true);
            if (t != null) t.text = Get(key);
        }
    }
}
