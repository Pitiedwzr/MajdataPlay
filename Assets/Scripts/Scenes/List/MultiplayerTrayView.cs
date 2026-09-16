using Cysharp.Threading.Tasks;
using LitMotion;
using MajdataPlay.Net;
using MajdataPlay.Game;
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay.Scenes.List
{
    internal sealed class MultiplayerTrayView : MonoBehaviour
    {
        // Dimensions
        const float TRAY_WIDTH = 420f;
        const float TRAY_HEIGHT = 740f;
        const float HANDLE_WIDTH = 56f;
        const float HANDLE_HEIGHT = 170f;
        const float SLIDE_DURATION = 0.32f;

        // Colors
        static readonly Color BG_COLOR = new(0.08f, 0.10f, 0.14f, 0.96f);
        static readonly Color PANEL_CARD_BG = new(0.13f, 0.16f, 0.22f, 0.90f);
        static readonly Color ACCENT_CYAN = new(0.00f, 0.82f, 1.00f, 1.00f);
        static readonly Color ACCENT_GREEN = new(0.18f, 0.80f, 0.44f, 1.00f);
        static readonly Color ACCENT_RED = new(0.92f, 0.26f, 0.26f, 1.00f);
        static readonly Color ACCENT_ORANGE = new(1.00f, 0.65f, 0.00f, 1.00f);
        static readonly Color TEXT_DIM = new(0.70f, 0.75f, 0.82f, 1.00f);

        // UI hierarchy references
        Canvas? _overlayCanvas;
        RectTransform? _trayRoot;
        Image? _handleStatusDot;
        TextMeshProUGUI? _handleLabel;

        // Disconnected view
        GameObject? _disconnectedGroup;
        TMP_InputField? _roomNameInput;
        TMP_InputField? _roomCodeInput;
        Button? _createRoomBtn;
        Button? _joinRoomBtn;

        // Connected view
        GameObject? _connectedGroup;
        TextMeshProUGUI? _roomTitleText;
        TextMeshProUGUI? _roomCodeBadgeText;
        TextMeshProUGUI? _roomStatsText;
        Transform? _membersContainer;
        Button? _leaveRoomBtn;

        // Status banner
        TextMeshProUGUI? _statusText;

        // State
        bool _isOpen;
        bool _isSubmitting;
        string _status = "Create a room or enter a six-character room code.";
        Color _statusColor = TEXT_DIM;
        MotionHandle _slideMotion;
        readonly List<MemberCardItem> _memberCards = new();

        void Awake()
        {
            BuildUI();
            MultiplayerSession.RoomStateChanged += OnRoomStateChanged;
            UpdateViewContent();
        }

        void OnDestroy()
        {
            MultiplayerSession.RoomStateChanged -= OnRoomStateChanged;
            _slideMotion.TryCancel();
            if (_overlayCanvas != null)
            {
                Destroy(_overlayCanvas.gameObject);
            }
        }

        void Update()
        {
            // Sync status if state changed externally
            var isConnected = MultiplayerSession.IsConnected;
            if (_connectedGroup != null && _disconnectedGroup != null)
            {
                if (isConnected != _connectedGroup.activeSelf)
                {
                    UpdateViewContent();
                }
            }
        }

        void BuildUI()
        {
            var font = Majdata<GameRuntime>.Instance?.Fonts?.Default;

            // 1. Overlay Canvas
            var canvasObj = new GameObject("MultiplayerTrayCanvas");
            _overlayCanvas = canvasObj.AddComponent<Canvas>();
            _overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _overlayCanvas.sortingOrder = 900;

            var scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;

            canvasObj.AddComponent<GraphicRaycaster>();

            // 2. Tray Root (Right aligned)
            var trayRootObj = new GameObject("TrayRoot", typeof(RectTransform));
            trayRootObj.transform.SetParent(canvasObj.transform, false);
            _trayRoot = (RectTransform)trayRootObj.transform;
            _trayRoot.anchorMin = new Vector2(1f, 0.5f);
            _trayRoot.anchorMax = new Vector2(1f, 0.5f);
            _trayRoot.pivot = new Vector2(0f, 0.5f);
            _trayRoot.sizeDelta = new Vector2(TRAY_WIDTH, TRAY_HEIGHT);
            _trayRoot.anchoredPosition = new Vector2(0f, 0f); // Closed by default

            // 3. Slide Handle Tab (Sticks out to the left of the tray)
            BuildHandleTab(trayRootObj.transform, font);

            // 4. Main Drawer Panel
            var drawerPanel = new GameObject("DrawerPanel", typeof(RectTransform), typeof(Image));
            drawerPanel.transform.SetParent(trayRootObj.transform, false);
            var drawerRt = (RectTransform)drawerPanel.transform;
            drawerRt.anchorMin = Vector2.zero;
            drawerRt.anchorMax = Vector2.one;
            drawerRt.sizeDelta = Vector2.zero;
            drawerRt.anchoredPosition = Vector2.zero;
            var drawerImg = drawerPanel.GetComponent<Image>();
            drawerImg.color = BG_COLOR;

            // Header bar
            BuildHeader(drawerPanel.transform, font);

            // Status message box
            BuildStatusBar(drawerPanel.transform, font);

            // Content container
            var contentObj = new GameObject("ContentContainer", typeof(RectTransform));
            contentObj.transform.SetParent(drawerPanel.transform, false);
            var contentRt = (RectTransform)contentObj.transform;
            contentRt.anchorMin = new Vector2(0f, 0f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.sizeDelta = new Vector2(-36f, -145f);
            contentRt.anchoredPosition = new Vector2(0f, -40f);

            // 5. Disconnected Group
            BuildDisconnectedGroup(contentObj.transform, font);

            // 6. Connected Group
            BuildConnectedGroup(contentObj.transform, font);
        }

        void BuildHandleTab(Transform parent, TMP_FontAsset? font)
        {
            var handleObj = new GameObject("HandleTab", typeof(RectTransform), typeof(Image), typeof(Button));
            handleObj.transform.SetParent(parent, false);
            var handleRt = (RectTransform)handleObj.transform;
            handleRt.anchorMin = new Vector2(0f, 0.5f);
            handleRt.anchorMax = new Vector2(0f, 0.5f);
            handleRt.pivot = new Vector2(1f, 0.5f);
            handleRt.sizeDelta = new Vector2(HANDLE_WIDTH, HANDLE_HEIGHT);
            handleRt.anchoredPosition = Vector2.zero;

            var handleImg = handleObj.GetComponent<Image>();
            handleImg.color = new Color(0.10f, 0.12f, 0.18f, 0.95f);

            var handleBtn = handleObj.GetComponent<Button>();
            handleBtn.targetGraphic = handleImg;
            handleBtn.onClick.AddListener(ToggleTray);

            // Status Dot
            var dotObj = new GameObject("StatusDot", typeof(RectTransform), typeof(Image));
            dotObj.transform.SetParent(handleObj.transform, false);
            var dotRt = (RectTransform)dotObj.transform;
            dotRt.anchorMin = new Vector2(0.5f, 1f);
            dotRt.anchorMax = new Vector2(0.5f, 1f);
            dotRt.anchoredPosition = new Vector2(0f, -22f);
            dotRt.sizeDelta = new Vector2(16f, 16f);
            _handleStatusDot = dotObj.GetComponent<Image>();
            _handleStatusDot.color = TEXT_DIM;

            // Handle Text
            var textObj = new GameObject("HandleLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObj.transform.SetParent(handleObj.transform, false);
            var textRt = (RectTransform)textObj.transform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.sizeDelta = new Vector2(0f, -45f);
            textRt.anchoredPosition = new Vector2(0f, -15f);

            _handleLabel = textObj.GetComponent<TextMeshProUGUI>();
            _handleLabel.text = "联\n机";
            _handleLabel.fontSize = 20;
            _handleLabel.alignment = TextAlignmentOptions.Center;
            _handleLabel.color = Color.white;
            if (font != null) _handleLabel.font = font;
        }

        void BuildHeader(Transform parent, TMP_FontAsset? font)
        {
            var headerObj = new GameObject("Header", typeof(RectTransform));
            headerObj.transform.SetParent(parent, false);
            var headerRt = (RectTransform)headerObj.transform;
            headerRt.anchorMin = new Vector2(0f, 1f);
            headerRt.anchorMax = new Vector2(1f, 1f);
            headerRt.pivot = new Vector2(0.5f, 1f);
            headerRt.sizeDelta = new Vector2(-36f, 54f);
            headerRt.anchoredPosition = new Vector2(0f, -12f);

            // Title
            var titleObj = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI));
            titleObj.transform.SetParent(headerObj.transform, false);
            var titleRt = (RectTransform)titleObj.transform;
            titleRt.anchorMin = Vector2.zero;
            titleRt.anchorMax = new Vector2(0.8f, 1f);
            titleRt.sizeDelta = Vector2.zero;
            titleRt.anchoredPosition = Vector2.zero;

            var titleText = titleObj.GetComponent<TextMeshProUGUI>();
            titleText.text = "MULTIPLAYER";
            titleText.fontSize = 26;
            titleText.fontStyle = FontStyles.Bold;
            titleText.color = ACCENT_CYAN;
            titleText.alignment = TextAlignmentOptions.MidlineLeft;
            if (font != null) titleText.font = font;

            // Close button
            var (closeBtn, _) = CreateButton(headerObj.transform, "CloseBtn", "✕", new Color(0.2f, 0.24f, 0.3f, 0.8f), Color.white, font);
            var closeRt = (RectTransform)closeBtn.transform;
            closeRt.anchorMin = new Vector2(1f, 0.5f);
            closeRt.anchorMax = new Vector2(1f, 0.5f);
            closeRt.pivot = new Vector2(1f, 0.5f);
            closeRt.sizeDelta = new Vector2(40f, 40f);
            closeRt.anchoredPosition = Vector2.zero;
            closeBtn.onClick.AddListener(() => SetOpen(false));
        }

        void BuildStatusBar(Transform parent, TMP_FontAsset? font)
        {
            var statusBoxObj = new GameObject("StatusBar", typeof(RectTransform), typeof(Image));
            statusBoxObj.transform.SetParent(parent, false);
            var statusRt = (RectTransform)statusBoxObj.transform;
            statusRt.anchorMin = new Vector2(0f, 1f);
            statusRt.anchorMax = new Vector2(1f, 1f);
            statusRt.pivot = new Vector2(0.5f, 1f);
            statusRt.sizeDelta = new Vector2(-36f, 44f);
            statusRt.anchoredPosition = new Vector2(0f, -68f);

            var statusImg = statusBoxObj.GetComponent<Image>();
            statusImg.color = new Color(0.12f, 0.14f, 0.19f, 0.9f);

            var textObj = new GameObject("StatusText", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObj.transform.SetParent(statusBoxObj.transform, false);
            var textRt = (RectTransform)textObj.transform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.sizeDelta = new Vector2(-20f, 0f);
            textRt.anchoredPosition = Vector2.zero;

            _statusText = textObj.GetComponent<TextMeshProUGUI>();
            _statusText.text = _status;
            _statusText.fontSize = 15;
            _statusText.color = _statusColor;
            _statusText.alignment = TextAlignmentOptions.Center;
            _statusText.overflowMode = TextOverflowModes.Ellipsis;
            if (font != null) _statusText.font = font;
        }

        void BuildDisconnectedGroup(Transform parent, TMP_FontAsset? font)
        {
            _disconnectedGroup = new GameObject("DisconnectedGroup", typeof(RectTransform));
            _disconnectedGroup.transform.SetParent(parent, false);
            var rt = (RectTransform)_disconnectedGroup.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;

            // SECTION 1: Create Room Card
            var createCard = CreateSectionCard(_disconnectedGroup.transform, "CreateCard", -20f, 210f);
            CreateLabel(createCard.transform, "CREATE A ROOM", 16, ACCENT_CYAN, new Vector2(0f, -16f), font);

            CreateLabel(createCard.transform, "Room Name", 14, TEXT_DIM, new Vector2(0f, -44f), font);
            _roomNameInput = CreateInputField(createCard.transform, "RoomNameInput", "Multiplayer room", "Multiplayer room", 48, new Vector2(0f, -80f), font);

            var (createBtn, _) = CreateButton(createCard.transform, "CreateBtn", "Create Room", ACCENT_CYAN, Color.black, font);
            _createRoomBtn = createBtn;
            var createBtnRt = (RectTransform)createBtn.transform;
            createBtnRt.anchorMin = new Vector2(0f, 0f);
            createBtnRt.anchorMax = new Vector2(1f, 0f);
            createBtnRt.sizeDelta = new Vector2(-30f, 44f);
            createBtnRt.anchoredPosition = new Vector2(0f, 32f);
            createBtn.onClick.AddListener(() => CreateRoomAsync().Forget());

            // SECTION 2: Join Room Card
            var joinCard = CreateSectionCard(_disconnectedGroup.transform, "JoinCard", -255f, 210f);
            CreateLabel(joinCard.transform, "JOIN EXISTING ROOM", 16, ACCENT_GREEN, new Vector2(0f, -16f), font);

            CreateLabel(joinCard.transform, "Room Code", 14, TEXT_DIM, new Vector2(0f, -44f), font);
            _roomCodeInput = CreateInputField(joinCard.transform, "RoomCodeInput", string.Empty, "6-LETTER CODE", 6, new Vector2(0f, -80f), font);
            _roomCodeInput.onValueChanged.AddListener(val => _roomCodeInput.text = val.ToUpperInvariant());

            var (joinBtn, _) = CreateButton(joinCard.transform, "JoinBtn", "Join Room", ACCENT_GREEN, Color.black, font);
            _joinRoomBtn = joinBtn;
            var joinBtnRt = (RectTransform)joinBtn.transform;
            joinBtnRt.anchorMin = new Vector2(0f, 0f);
            joinBtnRt.anchorMax = new Vector2(1f, 0f);
            joinBtnRt.sizeDelta = new Vector2(-30f, 44f);
            joinBtnRt.anchoredPosition = new Vector2(0f, 32f);
            joinBtn.onClick.AddListener(() => JoinRoomAsync().Forget());
        }

        void BuildConnectedGroup(Transform parent, TMP_FontAsset? font)
        {
            _connectedGroup = new GameObject("ConnectedGroup", typeof(RectTransform));
            _connectedGroup.transform.SetParent(parent, false);
            var rt = (RectTransform)_connectedGroup.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;

            // Room Info Header Card
            var roomCard = CreateSectionCard(_connectedGroup.transform, "RoomCard", -15f, 130f);

            var titleObj = new GameObject("RoomName", typeof(RectTransform), typeof(TextMeshProUGUI));
            titleObj.transform.SetParent(roomCard.transform, false);
            var titleRt = (RectTransform)titleObj.transform;
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.sizeDelta = new Vector2(-30f, 32f);
            titleRt.anchoredPosition = new Vector2(0f, -12f);
            _roomTitleText = titleObj.GetComponent<TextMeshProUGUI>();
            _roomTitleText.fontSize = 22;
            _roomTitleText.fontStyle = FontStyles.Bold;
            _roomTitleText.color = Color.white;
            if (font != null) _roomTitleText.font = font;

            var codeObj = new GameObject("RoomCodeBadge", typeof(RectTransform), typeof(TextMeshProUGUI));
            codeObj.transform.SetParent(roomCard.transform, false);
            var codeRt = (RectTransform)codeObj.transform;
            codeRt.anchorMin = new Vector2(0f, 1f);
            codeRt.anchorMax = new Vector2(1f, 1f);
            codeRt.pivot = new Vector2(0.5f, 1f);
            codeRt.sizeDelta = new Vector2(-30f, 26f);
            codeRt.anchoredPosition = new Vector2(0f, -48f);
            _roomCodeBadgeText = codeObj.GetComponent<TextMeshProUGUI>();
            _roomCodeBadgeText.fontSize = 17;
            _roomCodeBadgeText.color = ACCENT_CYAN;
            if (font != null) _roomCodeBadgeText.font = font;

            var statsObj = new GameObject("RoomStats", typeof(RectTransform), typeof(TextMeshProUGUI));
            statsObj.transform.SetParent(roomCard.transform, false);
            var statsRt = (RectTransform)statsObj.transform;
            statsRt.anchorMin = new Vector2(0f, 1f);
            statsRt.anchorMax = new Vector2(1f, 1f);
            statsRt.pivot = new Vector2(0.5f, 1f);
            statsRt.sizeDelta = new Vector2(-30f, 24f);
            statsRt.anchoredPosition = new Vector2(0f, -80f);
            _roomStatsText = statsObj.GetComponent<TextMeshProUGUI>();
            _roomStatsText.fontSize = 15;
            _roomStatsText.color = TEXT_DIM;
            if (font != null) _roomStatsText.font = font;

            // Members List Container
            var membersObj = new GameObject("MembersList", typeof(RectTransform), typeof(VerticalLayoutGroup));
            membersObj.transform.SetParent(_connectedGroup.transform, false);
            var membersRt = (RectTransform)membersObj.transform;
            membersRt.anchorMin = new Vector2(0f, 0f);
            membersRt.anchorMax = new Vector2(1f, 1f);
            membersRt.sizeDelta = new Vector2(0f, -230f);
            membersRt.anchoredPosition = new Vector2(0f, -20f);

            var vlg = membersObj.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 8f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            _membersContainer = membersObj.transform;

            // Pre-create 4 member cards
            for (var i = 0; i < 4; i++)
            {
                var card = CreateMemberCard(_membersContainer, font);
                _memberCards.Add(card);
            }

            // Leave Room Button at bottom
            var (leaveBtn, _) = CreateButton(_connectedGroup.transform, "LeaveBtn", "Leave Room", ACCENT_RED, Color.white, font);
            _leaveRoomBtn = leaveBtn;
            var leaveRt = (RectTransform)leaveBtn.transform;
            leaveRt.anchorMin = new Vector2(0f, 0f);
            leaveRt.anchorMax = new Vector2(1f, 0f);
            leaveRt.sizeDelta = new Vector2(0f, 48f);
            leaveRt.anchoredPosition = new Vector2(0f, 28f);
            leaveBtn.onClick.AddListener(OnLeaveRoomClicked);
        }

        MemberCardItem CreateMemberCard(Transform parent, TMP_FontAsset? font)
        {
            var cardObj = new GameObject("MemberCard", typeof(RectTransform), typeof(Image));
            cardObj.transform.SetParent(parent, false);
            var cardRt = (RectTransform)cardObj.transform;
            cardRt.sizeDelta = new Vector2(0f, 54f);

            var cardImg = cardObj.GetComponent<Image>();
            cardImg.color = PANEL_CARD_BG;

            // Username
            var nameObj = new GameObject("Username", typeof(RectTransform), typeof(TextMeshProUGUI));
            nameObj.transform.SetParent(cardObj.transform, false);
            var nameRt = (RectTransform)nameObj.transform;
            nameRt.anchorMin = new Vector2(0f, 0f);
            nameRt.anchorMax = new Vector2(0.55f, 1f);
            nameRt.sizeDelta = new Vector2(-15f, 0f);
            nameRt.anchoredPosition = new Vector2(15f, 0f);
            var nameText = nameObj.GetComponent<TextMeshProUGUI>();
            nameText.fontSize = 18;
            nameText.color = Color.white;
            nameText.alignment = TextAlignmentOptions.MidlineLeft;
            nameText.overflowMode = TextOverflowModes.Ellipsis;
            if (font != null) nameText.font = font;

            // Difficulty Tag
            var diffTagObj = new GameObject("DiffTag", typeof(RectTransform), typeof(Image));
            diffTagObj.transform.SetParent(cardObj.transform, false);
            var diffRt = (RectTransform)diffTagObj.transform;
            diffRt.anchorMin = new Vector2(0.56f, 0.5f);
            diffRt.anchorMax = new Vector2(0.56f, 0.5f);
            diffRt.pivot = new Vector2(0f, 0.5f);
            diffRt.sizeDelta = new Vector2(74f, 28f);
            diffRt.anchoredPosition = Vector2.zero;
            var diffImg = diffTagObj.GetComponent<Image>();
            diffImg.color = ACCENT_CYAN;

            var diffTextObj = new GameObject("DiffText", typeof(RectTransform), typeof(TextMeshProUGUI));
            diffTextObj.transform.SetParent(diffTagObj.transform, false);
            var diffTextRt = (RectTransform)diffTextObj.transform;
            diffTextRt.anchorMin = Vector2.zero;
            diffTextRt.anchorMax = Vector2.one;
            diffTextRt.sizeDelta = Vector2.zero;
            diffTextRt.anchoredPosition = Vector2.zero;
            var diffText = diffTextObj.GetComponent<TextMeshProUGUI>();
            diffText.fontSize = 14;
            diffText.fontStyle = FontStyles.Bold;
            diffText.alignment = TextAlignmentOptions.Center;
            diffText.color = Color.black;
            if (font != null) diffText.font = font;

            // Status Pill
            var statusObj = new GameObject("StatusText", typeof(RectTransform), typeof(TextMeshProUGUI));
            statusObj.transform.SetParent(cardObj.transform, false);
            var statusRt = (RectTransform)statusObj.transform;
            statusRt.anchorMin = new Vector2(0.78f, 0f);
            statusRt.anchorMax = new Vector2(1f, 1f);
            statusRt.sizeDelta = new Vector2(-10f, 0f);
            statusRt.anchoredPosition = new Vector2(-5f, 0f);
            var statusText = statusObj.GetComponent<TextMeshProUGUI>();
            statusText.fontSize = 14;
            statusText.alignment = TextAlignmentOptions.MidlineRight;
            if (font != null) statusText.font = font;

            cardObj.SetActive(false);
            return new MemberCardItem
            {
                Root = cardObj,
                UsernameText = nameText,
                DiffBadgeImage = diffImg,
                DiffBadgeText = diffText,
                StatusText = statusText
            };
        }

        void OnRoomStateChanged(MultiplayerSession.RoomState state)
        {
            UpdateViewContent();
        }

        void UpdateViewContent()
        {
            var isConnected = MultiplayerSession.IsConnected;
            if (_disconnectedGroup != null) _disconnectedGroup.SetActive(!isConnected);
            if (_connectedGroup != null) _connectedGroup.SetActive(isConnected);

            if (_handleStatusDot != null)
            {
                _handleStatusDot.color = isConnected ? ACCENT_GREEN : TEXT_DIM;
            }

            if (_statusText != null)
            {
                _statusText.text = _status;
                _statusText.color = _statusColor;
            }

            if (!isConnected)
            {
                if (_handleLabel != null) _handleLabel.text = "联\n机";
                return;
            }

            var room = MultiplayerSession.CurrentRoom;
            if (room is null)
            {
                if (_roomTitleText != null) _roomTitleText.text = "Multiplayer";
                if (_roomCodeBadgeText != null) _roomCodeBadgeText.text = "Waiting for room...";
                if (_roomStatsText != null) _roomStatsText.text = string.Empty;
                return;
            }

            if (_handleLabel != null)
            {
                _handleLabel.text = string.IsNullOrEmpty(room.Value.Code) ? "房\n间" : room.Value.Code;
            }

            if (_roomTitleText != null) _roomTitleText.text = room.Value.Name;
            if (_roomCodeBadgeText != null) _roomCodeBadgeText.text = $"ROOM CODE: {room.Value.Code}";
            if (_roomStatsText != null)
            {
                _roomStatsText.text = $"Players: {room.Value.PlayerCount}  •  Ready: {room.Value.ReadyPlayerCount}/{room.Value.PlayerCount}";
            }

            // Update member cards
            var members = room.Value.Members ?? Array.Empty<MultiplayerSession.RoomMember>();
            for (var i = 0; i < _memberCards.Count; i++)
            {
                var card = _memberCards[i];
                if (i < members.Length)
                {
                    var m = members[i];
                    card.Root.SetActive(true);
                    card.UsernameText.text = string.IsNullOrEmpty(m.Username) ? "Player" : m.Username;

                    var (diffName, diffColor) = GetDifficultyDisplay(m.Difficulty);
                    card.DiffBadgeText.text = diffName;
                    card.DiffBadgeImage.color = diffColor;

                    if (!m.IsConnected)
                    {
                        card.StatusText.text = "Offline";
                        card.StatusText.color = TEXT_DIM;
                    }
                    else if (m.IsRoundComplete)
                    {
                        card.StatusText.text = "Finished";
                        card.StatusText.color = new Color(0.7f, 0.4f, 1f);
                    }
                    else if (m.IsReady)
                    {
                        card.StatusText.text = "Ready";
                        card.StatusText.color = ACCENT_GREEN;
                    }
                    else
                    {
                        card.StatusText.text = "Selecting";
                        card.StatusText.color = ACCENT_ORANGE;
                    }
                }
                else
                {
                    card.Root.SetActive(false);
                }
            }
        }

        static (string Name, Color Color) GetDifficultyDisplay(int difficulty)
        {
            return difficulty switch
            {
                0 => ("EASY", new Color(0.18f, 0.52f, 0.92f)),
                1 => ("BASIC", new Color(0.25f, 0.72f, 0.28f)),
                2 => ("ADVANCE", new Color(0.98f, 0.65f, 0.05f)),
                3 => ("EXPERT", new Color(0.92f, 0.20f, 0.20f)),
                4 => ("MASTER", new Color(0.65f, 0.16f, 0.85f)),
                5 => ("RE:MAS", new Color(0.92f, 0.85f, 0.95f)),
                _ => ("ORIG", Color.white)
            };
        }

        public void ToggleTray()
        {
            SetOpen(!_isOpen);
        }

        public void SetOpen(bool open)
        {
            if (_isOpen == open || _trayRoot == null)
            {
                return;
            }
            _isOpen = open;
            PlaySFX("touch.wav");

            _slideMotion.TryCancel();
            var startX = _trayRoot.anchoredPosition.x;
            var targetX = _isOpen ? -TRAY_WIDTH : 0f;

            _slideMotion = LMotion.Create(startX, targetX, SLIDE_DURATION)
                                  .WithEase(Ease.OutCubic)
                                  .Bind(x => _trayRoot.anchoredPosition = new Vector2(x, _trayRoot.anchoredPosition.y));
        }

        void OnLeaveRoomClicked()
        {
            PlaySFX("answer.wav");
            MultiplayerSession.Disconnect();
            SetStatus("Left room.", TEXT_DIM);
            UpdateViewContent();
        }

        async UniTaskVoid CreateRoomAsync()
        {
            var roomName = _roomNameInput?.text?.Trim();
            if (string.IsNullOrEmpty(roomName))
            {
                roomName = "Multiplayer room";
            }
            PlaySFX("touch.wav");
            await SubmitAsync(() => MultiplayerSession.CreateRoomAsync(GetEndpoint(), roomName));
        }

        async UniTaskVoid JoinRoomAsync()
        {
            var code = _roomCodeInput?.text?.Trim().ToUpperInvariant() ?? string.Empty;
            if (code.Length != 6)
            {
                SetStatus("Room codes must contain 6 characters.", ACCENT_ORANGE);
                return;
            }
            PlaySFX("touch.wav");
            await SubmitAsync(() => MultiplayerSession.JoinRoomAsync(GetEndpoint(), code));
        }

        async UniTask SubmitAsync(Func<UniTask<MultiplayerSession.RoomInfo>> action)
        {
            try
            {
                SetSubmitting(true);
                SetStatus("Connecting...", ACCENT_CYAN);
                var room = await action();
                SetStatus($"Connected to {room.Name} ({room.Code}).", ACCENT_GREEN);
                UpdateViewContent();
            }
            catch (Exception exception)
            {
                SetStatus($"Connection failed: {exception.Message}", ACCENT_RED);
            }
            finally
            {
                SetSubmitting(false);
            }
        }

        void SetSubmitting(bool submitting)
        {
            _isSubmitting = submitting;
            if (_createRoomBtn != null) _createRoomBtn.interactable = !submitting;
            if (_joinRoomBtn != null) _joinRoomBtn.interactable = !submitting;
        }

        void SetStatus(string message, Color color)
        {
            _status = message;
            _statusColor = color;
            if (_statusText != null)
            {
                _statusText.text = message;
                _statusText.color = color;
            }
        }

        static void PlaySFX(string name)
        {
            try
            {
                MajInstances.AudioManager.PlaySFX(name);
            }
            catch
            {
                // Audio failure shouldn't break UI
            }
        }

        static ApiEndpoint GetEndpoint()
        {
            return MajEnv.ApiEndpoints.FirstOrDefault(endpoint => endpoint.RuntimeConfig.IsLoggedIn)
                ?? throw new InvalidOperationException("Log in to an online server before joining multiplayer.");
        }

        // --- Helper Factories ---

        static GameObject CreateSectionCard(Transform parent, string name, float posY, float height)
        {
            var card = new GameObject(name, typeof(RectTransform), typeof(Image));
            card.transform.SetParent(parent, false);
            var rt = (RectTransform)card.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, height);
            rt.anchoredPosition = new Vector2(0f, posY);

            var img = card.GetComponent<Image>();
            img.color = PANEL_CARD_BG;
            return card;
        }

        static void CreateLabel(Transform parent, string text, float size, Color color, Vector2 anchoredPos, TMP_FontAsset? font)
        {
            var labelObj = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObj.transform.SetParent(parent, false);
            var rt = (RectTransform)labelObj.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(-30f, size + 8f);
            rt.anchoredPosition = anchoredPos;

            var tmp = labelObj.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            if (font != null) tmp.font = font;
        }

        static (Button btn, TextMeshProUGUI text) CreateButton(Transform parent, string name, string label, Color bgColor, Color textColor, TMP_FontAsset? font)
        {
            var btnObj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            btnObj.transform.SetParent(parent, false);

            var img = btnObj.GetComponent<Image>();
            img.color = bgColor;

            var btn = btnObj.GetComponent<Button>();
            btn.targetGraphic = img;

            var colors = btn.colors;
            colors.normalColor = bgColor;
            colors.highlightedColor = bgColor * 1.15f;
            colors.pressedColor = bgColor * 0.85f;
            colors.disabledColor = new Color(bgColor.r, bgColor.g, bgColor.b, 0.4f);
            btn.colors = colors;

            var textObj = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObj.transform.SetParent(btnObj.transform, false);
            var textRt = (RectTransform)textObj.transform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.sizeDelta = Vector2.zero;
            textRt.anchoredPosition = Vector2.zero;

            var tmp = textObj.GetComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.color = textColor;
            tmp.fontSize = 18;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            if (font != null) tmp.font = font;

            return (btn, tmp);
        }

        static TMP_InputField CreateInputField(Transform parent, string name, string initialText, string placeholder, int charLimit, Vector2 anchoredPos, TMP_FontAsset? font)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            root.transform.SetParent(parent, false);
            var rootRt = (RectTransform)root.transform;
            rootRt.anchorMin = new Vector2(0f, 1f);
            rootRt.anchorMax = new Vector2(1f, 1f);
            rootRt.pivot = new Vector2(0.5f, 1f);
            rootRt.sizeDelta = new Vector2(-30f, 42f);
            rootRt.anchoredPosition = anchoredPos;

            var bgImg = root.GetComponent<Image>();
            bgImg.color = new Color(0.08f, 0.10f, 0.14f, 0.95f);

            var input = root.GetComponent<TMP_InputField>();
            input.targetGraphic = bgImg;
            if (charLimit > 0) input.characterLimit = charLimit;

            var textArea = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
            textArea.transform.SetParent(root.transform, false);
            var textAreaRt = (RectTransform)textArea.transform;
            textAreaRt.anchorMin = Vector2.zero;
            textAreaRt.anchorMax = Vector2.one;
            textAreaRt.sizeDelta = new Vector2(-20f, -8f);
            textAreaRt.anchoredPosition = Vector2.zero;

            var placeholderObj = new GameObject("Placeholder", typeof(RectTransform), typeof(TextMeshProUGUI));
            placeholderObj.transform.SetParent(textArea.transform, false);
            var placeholderRt = (RectTransform)placeholderObj.transform;
            placeholderRt.anchorMin = Vector2.zero;
            placeholderRt.anchorMax = Vector2.one;
            placeholderRt.sizeDelta = Vector2.zero;
            placeholderRt.anchoredPosition = Vector2.zero;
            var placeholderText = placeholderObj.GetComponent<TextMeshProUGUI>();
            placeholderText.text = placeholder;
            placeholderText.fontSize = 16;
            placeholderText.color = new Color(0.5f, 0.55f, 0.65f, 0.5f);
            placeholderText.alignment = TextAlignmentOptions.MidlineLeft;
            if (font != null) placeholderText.font = font;

            var textObj = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObj.transform.SetParent(textArea.transform, false);
            var textRt = (RectTransform)textObj.transform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.sizeDelta = Vector2.zero;
            textRt.anchoredPosition = Vector2.zero;
            var text = textObj.GetComponent<TextMeshProUGUI>();
            text.text = initialText;
            text.fontSize = 16;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            if (font != null) text.font = font;

            input.textViewport = textAreaRt;
            input.textComponent = text;
            input.placeholder = placeholderText;
            if (font != null) input.fontAsset = font;
            input.text = initialText;

            return input;
        }

        struct MemberCardItem
        {
            public GameObject Root;
            public TextMeshProUGUI UsernameText;
            public Image DiffBadgeImage;
            public TextMeshProUGUI DiffBadgeText;
            public TextMeshProUGUI StatusText;
        }
    }
}
