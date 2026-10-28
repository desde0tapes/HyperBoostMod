using BepInEx;
using BepInEx.Configuration;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.IO;
using System.Text;
using System;
using System.Collections;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Reptile;
using HarmonyLib;
using System.Runtime.CompilerServices;

namespace HyperBoostMod
{
    [BepInPlugin("com.desdezerotapes.hyperboost", "Hyper Boost Mod", "1.0.3")]
    [BepInDependency("com.Dragsun.BunchOfEmotes", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public static Plugin Instance;
        public static ManualLogSource Log;

        public enum CameraDragStyle { Lerp, Slerp, Mixed }

        public enum HyperBoostFont
        {
            _2070POLYESTER_ALP53,
            DFSoge5,
            elements,
            FerriteCoreDX_Black,
            FORCED_SQUARE,
            forgotten_futurist_rg,
            GEIST_RND,
            induction,
            KARNIVOB,
            KORATAKI_REGULAR,
            ModernDOS8x16,
            Pitviper,
            Reality_Pursuit_NC,
            V5PRD_ProphitDot
        }

        // Configs
        public ConfigEntry<bool> IsEnabled;
        public ConfigEntry<bool> HyperBoostEnabled;
        public ConfigEntry<bool> HyperJumpEnabled;
        public ConfigEntry<bool> InfiniteHyperBoost;
        public ConfigEntry<bool> QteModeEnabled;

        public ConfigEntry<float> IconPosX;
        public ConfigEntry<float> IconPosY;
        public ConfigEntry<float> TextOffsetX;
        public ConfigEntry<float> TextOffsetY;
        public ConfigEntry<HyperBoostFont> UIFont;
        public ConfigEntry<bool> AbbreviateNumbers;

        // Movement Configs
        public ConfigEntry<float> ChargingDelayTime;
        public ConfigEntry<float> ChargingFallSpeed;
        public ConfigEntry<float> ChargingFallBlendTime;
        public ConfigEntry<float> BoostStartAmount;
        public ConfigEntry<float> BoostStartCap;
        public ConfigEntry<int> BoostsPerHold;

        // On-Foot Animations
        public ConfigEntry<string> OnFootChargingAnim;
        public ConfigEntry<string> OnFootGroundAnim;
        public ConfigEntry<string> OnFootAirAnim;

        // BMX Animations
        public ConfigEntry<string> BmxChargingAnim;
        public ConfigEntry<string> BmxGroundAnim;
        public ConfigEntry<string> BmxAirAnim;

        // Skateboard Animations
        public ConfigEntry<string> SkateboardChargingAnim;
        public ConfigEntry<string> SkateboardGroundAnim;
        public ConfigEntry<string> SkateboardAirAnim;

        // Inline Skate Animations
        public ConfigEntry<string> InlineChargingAnim;
        public ConfigEntry<string> InlineGroundAnim;
        public ConfigEntry<string> InlineAirAnim;

        // Camera Drag Configs
        public ConfigEntry<bool> CameraDragEnabled;
        public ConfigEntry<float> CameraDragDuration;
        public ConfigEntry<CameraDragStyle> CameraDragStyleConfig;

        // Visual and Audio Configs
        public ConfigEntry<bool> MirrorBoostParticles;

        // HyperJump Configs
        public ConfigEntry<float> HyperJumpSpeed;

        // Paths
        private string _modFolder;
        private string _iconPath;
        private string _overlayPath;
        private string _jsonPath;

        // State Tracking
        private int _currentCacheId = 0;
        private bool _activeHyperGrind = false;
        private bool _activeHyperGrindIsJump = false;
        private bool _wasHyperBoostingLastFrame = false;
        public bool ResumeHyperBoostAfterTrick = false;

        // Deferred Save
        private bool _isDirty = false;
        private float _saveTimer = 0f;
        private const float SaveDelay = 2.0f;

        // File Watcher
        private FileSystemWatcher _fileWatcher;
        private volatile bool _dataNeedsReload = false;
        private float _reloadDebounceTimer = 0f;
        private const float ReloadDebounceTime = 0.2f;

        // UI Cache
        private GameObject _hudCanvasObj;
        private Image _iconImage;
        private Image _overlayImage;
        private TextMeshProUGUI _countText;
        private RectTransform _iconRect;
        private RectTransform _textRect;
        private TMP_FontAsset _gameFont;
        private Dictionary<string, TMP_FontAsset> _availableFonts = new Dictionary<string, TMP_FontAsset>();
        private TMP_FontAsset _polyesterFallbackFont;
        private bool _fontSearchAttempted = false;
        private float _fontSearchTimer = 0f;

        // QTE Assets
        public Dictionary<int, Sprite> QteSprites = new Dictionary<int, Sprite>();
        public Sprite QteCircleSprite;
        public AudioClip QteOkayClip;
        public AudioClip QteCompleteClip;
        public AudioClip QteWrongClip;

        // Custom SFX Assets
        public AudioClip BoostStartClip;
        public AudioClip BoostLoopClip;
        public AudioClip BoostStopClip;

        // Reflection Caches
        private static readonly Dictionary<string, FieldInfo> FieldCache = new Dictionary<string, FieldInfo>();
        private static readonly Dictionary<string, PropertyInfo> PropertyCache = new Dictionary<string, PropertyInfo>();
        private static readonly Dictionary<string, MethodInfo> MethodCache = new Dictionary<string, MethodInfo>();

        // Custom Audio Source Cache
        private static readonly ConditionalWeakTable<Player, AudioSource> CustomLoopSources = new ConditionalWeakTable<Player, AudioSource>();
        private static readonly ConditionalWeakTable<Player, AudioSource> CustomOneShotSources = new ConditionalWeakTable<Player, AudioSource>();

        private Vector2 _lastIconPos = new Vector2(-99999f, -99999f);
        private Vector2 _lastTextOffset = new Vector2(-99999f, -99999f);

        [Serializable]
        private class TelemetryCache
        {
            public int cache_id;
        }

        private void Awake()
        {
            Instance = this;
            Log = base.Logger;

            // Initialize config
            IsEnabled = Config.Bind("1. General", "Enabled", true, "Master switch for the HyperBoost mod.");
            QteModeEnabled = Config.Bind("1. General", "QTE Mode", false, "Replaces charging delay with a 4-button Quick Time Event.");
            HyperBoostEnabled = Config.Bind("1. General", "Enable HyperBoost", true, "Enable horizontal HyperBoost triggered via Boost + Slide.");
            HyperJumpEnabled = Config.Bind("1. General", "Enable HyperJump", true, "Enable vertical HyperJump triggered via Trick 1 + Trick 2 + Trick 3.");
            InfiniteHyperBoost = Config.Bind("1. General", "Infinite HyperBoost", false, "If true, HyperBoost units are infinite and display as ‡ (does not overwrite your actual saved unit count).");

            IconPosX = Config.Bind("2. UI Positioning", "Icon Position X", 100f, "X position of the Icon on screen.");
            IconPosY = Config.Bind("2. UI Positioning", "Icon Position Y", -100f, "Y position of the Icon on screen.");

            TextOffsetX = Config.Bind("2. UI Positioning", "Text Offset X", 70f, "X offset of the text relative to the icon.");
            TextOffsetY = Config.Bind("2. UI Positioning", "Text Offset Y", 0f, "Y offset of the text relative to the icon.");

            UIFont = Config.Bind("2. UI Positioning", "Counter Font", HyperBoostFont._2070POLYESTER_ALP53, "Select the font for the HyperBoost units counter.");
            UIFont.SettingChanged += (sender, args) =>
            {
                _fontSearchAttempted = false;
                _fontSearchTimer = 2.0f; // Retry font lookup immediately after a live setting change
            };

            AbbreviateNumbers = Config.Bind("2. UI Positioning", "Abbreviate Numbers", false, "Condense high unit numbers into abbreviations (e.g. 1450 becomes 1.4K).");
            AbbreviateNumbers.SettingChanged += (sender, args) => UpdateUIText();

            ChargingDelayTime = Config.Bind("4. Movement", "Charging Delay Time", 0.4f, "Time in seconds of charging delay (startBoost) before the hyperboost or hyperjump. Set to 0.0 to bypass completely.");
            ChargingFallSpeed = Config.Bind("4. Movement", "Charging Fall Speed", 5f, "Vertical speed during the charging delay while airborne. 0 = frozen, positive = fall downward, negative = move upward.");
            ChargingFallBlendTime = Config.Bind("4. Movement", "Charging Fall Blend Time", 1f, "Time in seconds to smoothly transition from the current vertical speed to Charging Fall Speed. Set to 0 for an immediate transition.");
            BoostStartAmount = Config.Bind("4. Movement", "HyperBoost Start Amount", 40f, "Amount of speed added when starting a hyper boost.");
            BoostStartCap = Config.Bind("4. Movement", "HyperBoost Start Cap", 45f, "Maximum speed that can be obtained from starting a hyper boost.");
            BoostsPerHold = Config.Bind("4. Movement", "HyperBoosts Per Hold", 1, "Amount of boosts consumed per sustained hold trigger.");

            // Animation Configs
            OnFootChargingAnim = Config.Bind("5a. On Foot Anims", "Charging Animation", "startBoost", "Charging animation.");
            OnFootGroundAnim = Config.Bind("5a. On Foot Anims", "Ground Boost Animation", "boostRun", "Animation for ground boost.");
            OnFootAirAnim = Config.Bind("5a. On Foot Anims", "Air Boost Animation", "boostRun", "Animation for ground boost.");

            BmxChargingAnim = Config.Bind("5b. BMX Anims", "Charging Animation", "startBoost", "Charging animation.");
            BmxGroundAnim = Config.Bind("5b. BMX Anims", "Ground Boost Animation", "boostRun", "Animation for ground boost.");
            BmxAirAnim = Config.Bind("5b. BMX Anims", "Air Boost Animation", "boostRun", "Animation for air boost.");

            SkateboardChargingAnim = Config.Bind("5c. Skateboard Anims", "Charging Animation", "startBoost", "Charging animation.");
            SkateboardGroundAnim = Config.Bind("5c. Skateboard Anims", "Ground Boost Animation", "boostRun", "Animation for ground boost.");
            SkateboardAirAnim = Config.Bind("5c. Skateboard Anims", "Air Boost Animation", "boostRun", "Animation for air boost.");

            InlineChargingAnim = Config.Bind("5d. Inline Skates Anims", "Charging Animation", "startBoost", "Charging animation.");
            InlineGroundAnim = Config.Bind("5d. Inline Skates Anims", "Ground Boost Animation", "boostRun", "Animation for ground boost.");
            InlineAirAnim = Config.Bind("5d. Inline Skates Anims", "Air Boost Animation", "boostRun", "Animation for air boost.");

            CameraDragEnabled = Config.Bind("6. Camera", "Camera Drag Effect", true, "Enable the cinematic drag effect when boosting.");
            CameraDragDuration = Config.Bind("6. Camera", "Camera Drag Duration", 1.25f, "Duration in seconds for the camera to catch up to the player.");
            CameraDragStyleConfig = Config.Bind("6. Camera", "Camera Drag Style", CameraDragStyle.Mixed, "Style of the camera drag. Lerp is straight, Slerp arcs around the world center, Mixed dynamically blends between them based on camera rotation change.");

            MirrorBoostParticles = Config.Bind("7. Visuals", "Mirror Boost Particles", true, "Clones the player's native jetpack boost particles. Compatible natively with GoVegan mod.");

            HyperJumpSpeed = Config.Bind("8. HyperJump", "HyperJump Speed", 40f, "The upward speed applied when vertical hyper jumping.");

            // Initialize optional Bunch Of Emotes support
            if (Chainloader.PluginInfos.ContainsKey("com.Dragsun.BunchOfEmotes"))
            {
                BunchOfEmotesSupport.Initialize();
            }

            // Set up mod paths
            _modFolder = Path.Combine(Paths.PluginPath, "HyperBoostMod");
            string iconFolder = Path.Combine(_modFolder, "Icons");

            _iconPath = Path.Combine(iconFolder, "HyperBoostUIIcon.png");
            _overlayPath = Path.Combine(iconFolder, "HyperBoostUILay.png");
            _jsonPath = Path.Combine(_modFolder, "scorecount.json");

            if (!Directory.Exists(_modFolder)) Directory.CreateDirectory(_modFolder);
            if (!Directory.Exists(iconFolder)) Directory.CreateDirectory(iconFolder);

            if (!File.Exists(_jsonPath)) WriteCacheIdToFile(0);

            LoadQTEAssets(iconFolder);
            StartCoroutine(LoadAudioAssets());

            SetupCustomHUD();
            ReadJsonData();
            SetupFileWatcher();

            // Apply Harmony patches
            try
            {
                Harmony harmony = new Harmony("com.desdezerotapes.hyperboost");
                harmony.PatchAll();
            }
            catch (Exception e)
            {
                Log.LogError($"[Harmony Error] Failed to apply patches: {e.Message}");
            }

            // Create QTE manager
            GameObject qteManagerObj = new GameObject("HyperBoostQTEManager");
            DontDestroyOnLoad(qteManagerObj);
            qteManagerObj.AddComponent<QTEManager>();

            Log.LogInfo("HyperBoostMod 1.0.3 Initialized (FPS Optimized). Ready to boost.");
        }

        private void LoadQTEAssets(string iconFolder)
        {
            Dictionary<int, string> mapping = new Dictionary<int, string>()
            {
                { 21, "D1.png" }, { 29, "D2.png" }, { 56, "D3.png" }, { 57, "D4.png" },
                { 12, "B1.png" }, { 65, "B2.png" }, { 7, "B3.png" }, { 15, "B4.png" }
            };

            foreach (var kvp in mapping)
            {
                string path = Path.Combine(iconFolder, kvp.Value);
                if (File.Exists(path))
                {
                    byte[] data = File.ReadAllBytes(path);
                    Texture2D tex = new Texture2D(200, 200);
                    tex.LoadImage(data);
                    QteSprites[kvp.Key] = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                }
            }

            string circlePath = Path.Combine(iconFolder, "Circle.png");
            if (File.Exists(circlePath))
            {
                byte[] data = File.ReadAllBytes(circlePath);
                Texture2D tex = new Texture2D(640, 640);
                tex.LoadImage(data);
                QteCircleSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            }
        }

        private IEnumerator LoadAudioAssets()
        {
            string soundFolder = Path.Combine(_modFolder, "Sounds");
            if (!Directory.Exists(soundFolder)) Directory.CreateDirectory(soundFolder);

            // Load QTE audio
            yield return StartCoroutine(LoadWav(Path.Combine(soundFolder, "QTEOkay.wav"), c => QteOkayClip = c));
            yield return StartCoroutine(LoadWav(Path.Combine(soundFolder, "QTEComplete.wav"), c => QteCompleteClip = c));
            yield return StartCoroutine(LoadWav(Path.Combine(soundFolder, "QTEWrong.wav"), c => QteWrongClip = c));

            // Load boost SFX
            yield return StartCoroutine(LoadWav(Path.Combine(soundFolder, "boostStart.wav"), c => BoostStartClip = c));
            yield return StartCoroutine(LoadWav(Path.Combine(soundFolder, "ultraBoostLoop.wav"), c => BoostLoopClip = c));
            yield return StartCoroutine(LoadWav(Path.Combine(soundFolder, "boostStop.wav"), c => BoostStopClip = c));
        }

        private IEnumerator LoadWav(string path, Action<AudioClip> callback)
        {
            if (!File.Exists(path)) yield break;
            using (UnityWebRequest uwr = UnityWebRequestMultimedia.GetAudioClip("file:///" + path.Replace("\\", "/"), AudioType.WAV))
            {
                yield return uwr.SendWebRequest();
                if (uwr.result == UnityWebRequest.Result.Success)
                {
                    callback(DownloadHandlerAudioClip.GetContent(uwr));
                }
            }
        }

        private void OnDisable()
        {
            FlushSave();
        }

        private void OnApplicationQuit()
        {
            FlushSave();
        }

        // Public API
        public static bool IsPlayerHyperBoosting(Player player)
        {
            if (player == null) return false;

            object currentAbility = GetMemberValue(player, "ability");
            bool inHyperBoostAbility = currentAbility != null && currentAbility.GetType().Name == "HyperBoostAbility";
            bool inHyperGrind = Instance != null && Instance._activeHyperGrind;

            return inHyperBoostAbility || inHyperGrind;
        }

        // Reflection Helpers
        public static object GetMemberValue(object obj, string memberName)
        {
            if (obj == null) return null;
            Type type = obj.GetType();
            string key = type.FullName + "." + memberName;

            if (!FieldCache.TryGetValue(key, out FieldInfo field))
            {
                field = type.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                FieldCache[key] = field;
            }
            if (field != null) return field.GetValue(obj);

            if (!PropertyCache.TryGetValue(key, out PropertyInfo prop))
            {
                prop = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                PropertyCache[key] = prop;
            }
            return prop?.GetValue(obj);
        }

        public static void SetMemberValue(object obj, string memberName, object value)
        {
            if (obj == null) return;
            Type type = obj.GetType();
            string key = type.FullName + "." + memberName;

            if (!FieldCache.TryGetValue(key, out FieldInfo field))
            {
                field = type.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                FieldCache[key] = field;
            }
            if (field != null)
            {
                field.SetValue(obj, value);
                return;
            }

            if (!PropertyCache.TryGetValue(key, out PropertyInfo prop))
            {
                prop = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                PropertyCache[key] = prop;
            }
            if (prop != null && prop.CanWrite) prop.SetValue(obj, value);
        }

        public static object InvokeMethodSafe(object obj, string methodName, object[] args)
        {
            if (obj == null) return null;
            Type type = obj.GetType();
            string key = type.FullName + "." + methodName;

            if (!MethodCache.TryGetValue(key, out MethodInfo method))
            {
                method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                MethodCache[key] = method;
            }

            if (method != null)
            {
                try
                {
                    return method.Invoke(obj, args);
                }
                catch (Exception e)
                {
                    if (Log != null) Log.LogWarning($"[HyperBoostMod] Failed to invoke {methodName}: {e.Message}");
                }
            }
            return null;
        }

        public static CharacterVisual GetCharacterVisual(Player player)
        {
            return GetMemberValue(player, "characterVisual") as CharacterVisual;
        }

        // Custom 2D SFX
        private static AudioSource GetCustomLoopSource(Player p)
        {
            if (p == null) return null;

            if (CustomLoopSources.TryGetValue(p, out AudioSource customSource) && customSource != null)
            {
                return customSource;
            }

            GameObject audioObj = new GameObject("HyperBoost_CustomLoopSource");
            audioObj.transform.SetParent(p.transform, false);
            customSource = audioObj.AddComponent<AudioSource>();

            if (Reptile.Core.Instance != null && Reptile.Core.Instance.gameplaySfxAudioSource != null)
            {
                customSource.outputAudioMixerGroup = Reptile.Core.Instance.gameplaySfxAudioSource.outputAudioMixerGroup;
            }
            else
            {
                AudioSource vanillaSource = GetMemberValue(p, "playerUltraBoostLoopAudioSource") as AudioSource;
                if (vanillaSource != null) customSource.outputAudioMixerGroup = vanillaSource.outputAudioMixerGroup;
            }

            customSource.playOnAwake = false;
            customSource.spatialBlend = 0f; // 2D audio
            customSource.bypassEffects = true;
            customSource.bypassListenerEffects = true;
            customSource.bypassReverbZones = true;

            CustomLoopSources.Add(p, customSource);
            return customSource;
        }

        private static AudioSource GetCustomOneShotSource(Player p)
        {
            if (p == null) return null;

            if (CustomOneShotSources.TryGetValue(p, out AudioSource customSource) && customSource != null)
            {
                return customSource;
            }

            GameObject audioObj = new GameObject("HyperBoost_CustomOneShotSource");
            audioObj.transform.SetParent(p.transform, false);
            customSource = audioObj.AddComponent<AudioSource>();

            if (Reptile.Core.Instance != null && Reptile.Core.Instance.gameplaySfxAudioSource != null)
            {
                customSource.outputAudioMixerGroup = Reptile.Core.Instance.gameplaySfxAudioSource.outputAudioMixerGroup;
            }
            else
            {
                AudioSource vanillaSource = GetMemberValue(p, "playerOneShotAudioSource") as AudioSource;
                if (vanillaSource != null) customSource.outputAudioMixerGroup = vanillaSource.outputAudioMixerGroup;
            }

            customSource.playOnAwake = false;
            customSource.spatialBlend = 0f; // 2D audio
            customSource.bypassEffects = true;
            customSource.bypassListenerEffects = true;
            customSource.bypassReverbZones = true;

            CustomOneShotSources.Add(p, customSource);
            return customSource;
        }

        public static void PlayBoostSfx(Player p, AudioClip clip, bool loop, float volume = 1f)
        {
            if (Instance == null || clip == null) return;

            try
            {
                AudioSource source = loop ? GetCustomLoopSource(p) : GetCustomOneShotSource(p);
                if (source == null) return;

                source.volume = volume;
                if (loop)
                {
                    source.clip = clip;
                    source.loop = true;
                    if (!source.isPlaying) source.Play();
                }
                else
                {
                    source.PlayOneShot(clip);
                }
            }
            catch (Exception e)
            {
                if (Log != null) Log.LogWarning($"[HyperBoostMod] Failed to play custom SFX: {e.Message}");
            }
        }

        public static void StopBoostLoopSfx(Player p)
        {
            try
            {
                if (p != null && CustomLoopSources.TryGetValue(p, out AudioSource customSource) && customSource != null)
                {
                    if (customSource.isPlaying) customSource.Stop();
                    customSource.clip = null;
                }
            }
            catch (Exception e)
            {
                if (Log != null) Log.LogWarning($"[HyperBoostMod] Failed to stop custom loop SFX: {e.Message}");
            }
        }

        public static object GetAudioManager()
        {
            if (Reptile.Core.Instance == null) return null;
            var prop = typeof(Reptile.Core).GetProperty("AudioManager", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop != null) return prop.GetValue(Reptile.Core.Instance);

            var field = typeof(Reptile.Core).GetField("AudioManager", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (field != null) return field.GetValue(Reptile.Core.Instance);

            return null;
        }

        public static void SetActiveAudioListenerSafe(AudioListener listener)
        {
            try
            {
                var audioManager = GetAudioManager();
                if (audioManager == null) return;

                MethodInfo setListenerMethod = audioManager.GetType().GetMethod("SetActiveAudioListener", BindingFlags.Public | BindingFlags.Instance);
                if (setListenerMethod != null)
                {
                    setListenerMethod.Invoke(audioManager, new object[] { listener });
                }
                else
                {
                    // Fall back to the field if the method is unavailable
                    FieldInfo activeListenerField = audioManager.GetType().GetField("activeAudioListener", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                    if (activeListenerField != null)
                    {
                        activeListenerField.SetValue(audioManager, listener);
                    }
                }
            }
            catch (Exception e)
            {
                if (Log != null) Log.LogWarning($"[HyperBoostMod] Failed to set Active Audio Listener: {e.Message}");
            }
        }

        public static void PlayVoiceSafe(Player p, string clipName)
        {
            try
            {
                var audioManager = GetAudioManager();
                if (audioManager == null) return;

                AudioSource source = GetMemberValue(p, "playerGameplayVoicesAudioSource") as AudioSource;
                object character = GetMemberValue(p, "character");
                object clipID = Enum.Parse(typeof(Reptile.AudioClipID), clipName, true);

                MethodInfo[] methods = audioManager.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public);
                foreach (MethodInfo method in methods)
                {
                    if (method.Name == "PlayVoice")
                    {
                        ParameterInfo[] parameters = method.GetParameters();
                        if (parameters.Length >= 4 && parameters[2].ParameterType == typeof(AudioClipID))
                        {
                            object[] args = new object[parameters.Length];
                            args[0] = Enum.ToObject(parameters[0].ParameterType, 0); // Default voice priority
                            args[1] = character;
                            args[2] = clipID;
                            args[3] = source;
                            for (int i = 4; i < parameters.Length; i++)
                            {
                                args[i] = parameters[i].DefaultValue;
                            }
                            method.Invoke(audioManager, args);
                            return;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                if (Log != null) Log.LogWarning($"[HyperBoostMod] Failed to play Voice {clipName}: {e.Message}");
            }
        }

        public static void SwapAnimatorControllerSafely(Animator animator, RuntimeAnimatorController targetController, int? overrideLayer0Hash = null, float overrideLayer0Time = 0f)
        {
            if (animator == null || targetController == null || animator.runtimeAnimatorController == targetController)
                return;

            int layerCount = animator.layerCount;
            var layerStates = new AnimatorStateInfo[layerCount];
            var layerWeights = new float[layerCount];

            for (int i = 0; i < layerCount; i++)
            {
                layerStates[i] = animator.GetCurrentAnimatorStateInfo(i);
                if (i > 0) layerWeights[i] = animator.GetLayerWeight(i);
            }

            animator.runtimeAnimatorController = targetController;
            animator.Rebind();

            int layer0Hash = overrideLayer0Hash ?? layerStates[0].fullPathHash;
            float layer0Time = overrideLayer0Hash.HasValue ? overrideLayer0Time : layerStates[0].normalizedTime;

            if (animator.HasState(0, layer0Hash))
                animator.Play(layer0Hash, 0, layer0Time);
            else
                animator.Play(Animator.StringToHash("idle"), 0, 0f);

            for (int i = 1; i < animator.layerCount && i < layerCount; i++)
            {
                animator.SetLayerWeight(i, layerWeights[i]);
                if (animator.HasState(i, layerStates[i].fullPathHash))
                    animator.Play(layerStates[i].fullPathHash, i, layerStates[i].normalizedTime);
            }

            animator.Update(0f);
        }

        private void SetupFileWatcher()
        {
            try
            {
                _fileWatcher = new FileSystemWatcher
                {
                    Path = Path.GetDirectoryName(_jsonPath),
                    Filter = Path.GetFileName(_jsonPath),
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime
                };

                _fileWatcher.Changed += (sender, e) => { if (!_isDirty) _dataNeedsReload = true; };
                _fileWatcher.Created += (sender, e) => { if (!_isDirty) _dataNeedsReload = true; };
                _fileWatcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                Log.LogError($"Failed to setup FileSystemWatcher: {ex.Message}");
            }
        }

        private void Update()
        {
            // Deferred save timer
            if (_isDirty)
            {
                _saveTimer -= Time.deltaTime;
                if (_saveTimer <= 0f)
                {
                    FlushSave();
                }
            }

            // Process external file changes
            if (_dataNeedsReload && !_isDirty)
            {
                _reloadDebounceTimer += Time.deltaTime;
                if (_reloadDebounceTimer >= ReloadDebounceTime)
                {
                    _dataNeedsReload = false;
                    _reloadDebounceTimer = 0f;
                    ReadJsonData();
                }
            }

            // Show the HUD only during gameplay
            Player player = WorldHandler.instance?.GetCurrentPlayer();
            bool inGameplayStage = player != null;

            if (_hudCanvasObj != null)
            {
                _hudCanvasObj.SetActive(IsEnabled.Value && inGameplayStage);
            }

            if (!IsEnabled.Value || !inGameplayStage) return;

            // Handle boost inputs and movement
            bool isDead = false;
            object isDeadVal = GetMemberValue(player, "isDead");
            if (isDeadVal is bool b) isDead = b;

            if (!isDead && Core.Instance != null && Core.Instance.GameInput != null)
            {
                GameInput gameInput = Core.Instance.GameInput;

                bool boostHeld = gameInput.GetButtonHeld(18); // Boost input
                bool jumpHeld = gameInput.GetButtonHeld(7);   // Jump input
                bool slideHeld = gameInput.GetButtonHeld(8);  // Slide input

                // HyperJump inputs
                bool trick1Held = gameInput.GetButtonHeld(15);
                bool trick2Held = gameInput.GetButtonHeld(12);
                bool trick3Held = gameInput.GetButtonHeld(65);

                bool boostNew = gameInput.GetButtonNew(18);
                bool jumpNew = gameInput.GetButtonNew(7);
                bool slideNew = gameInput.GetButtonNew(8);

                // Newly pressed HyperJump inputs
                bool trick1New = gameInput.GetButtonNew(15);
                bool trick2New = gameInput.GetButtonNew(12);
                bool trick3New = gameInput.GetButtonNew(65);

                bool triggerHyperBoost = HyperBoostEnabled.Value && ((boostHeld && slideNew) || (slideHeld && boostNew));

                // Trigger when all three are held and one is newly pressed
                bool triggerHyperJump = HyperJumpEnabled.Value && (trick1Held && trick2Held && trick3Held) && (trick1New || trick2New || trick3New);

                bool holdHyperBoost = HyperBoostEnabled.Value && boostHeld && slideHeld;

                // Hold requires all three inputs
                bool holdHyperJump = HyperJumpEnabled.Value && trick1Held && trick2Held && trick3Held;

                object currentAbility = GetMemberValue(player, "ability");
                bool isGrinding = currentAbility != null && currentAbility.GetType().Name.Contains("Grind");
                CharacterVisual charVis = GetCharacterVisual(player);

                // Boost trick break and resume
                bool isTricking = currentAbility != null && (
                    currentAbility.GetType().Name.Contains("Trick") ||
                    currentAbility.GetType().Name.Contains("Dash") ||
                    currentAbility.GetType().Name.Contains("Vert")
                );
                bool isHyperBoosting = currentAbility is HyperBoostAbility;

                if (ResumeHyperBoostAfterTrick)
                {
                    if (!holdHyperBoost)
                    {
                        ResumeHyperBoostAfterTrick = false;
                    }
                    else if (!isTricking && !isHyperBoosting)
                    {
                        ResumeHyperBoostAfterTrick = false;
                        triggerHyperBoost = true;
                    }
                }
                // End boost trick resume logic

                if (isGrinding && (holdHyperBoost || holdHyperJump) && !_activeHyperGrind && _wasHyperBoostingLastFrame)
                {
                    if (holdHyperBoost)
                    {
                        _activeHyperGrind = true;
                        _activeHyperGrindIsJump = false;

                        if (CameraDragEnabled.Value && CameraLagBehaviour.Instance != null)
                        {
                            CameraLagBehaviour.Instance.TriggerLag();
                        }

                        if (MirrorBoostParticles.Value && charVis != null)
                            charVis.SetBoostpackEffect(BoostpackEffectMode.BOOSTING);

                        PlayBoostSfx(player, Instance.BoostLoopClip, true, 0.6f);

                        if (GetMemberValue(player, "ringParticles") is ParticleSystem ps)
                        {
                            Quaternion oldRot = ps.transform.localRotation;
                            ps.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);
                            ps.Emit(5);
                            ps.transform.localRotation = oldRot;
                        }
                    }
                }

                // Check boost availability, including infinite mode
                if ((triggerHyperBoost || triggerHyperJump) && (_currentCacheId >= BoostsPerHold.Value || InfiniteHyperBoost.Value))
                {
                    bool isJumpTrigger = triggerHyperJump && !triggerHyperBoost;
                    if (triggerHyperJump) isJumpTrigger = true;

                    ConsumeHyperBoost(BoostsPerHold.Value);

                    if (!isGrinding || isJumpTrigger)
                    {
                        if (isGrinding) player.StopCurrentAbility();

                        if (!(currentAbility is HyperBoostAbility))
                        {
                            HyperBoostAbility hbAbility = new HyperBoostAbility(player);
                            hbAbility.isHyperJump = isJumpTrigger;
                            player.ActivateAbility(hbAbility);
                        }
                    }
                    else
                    {
                        _activeHyperGrind = true;
                        _activeHyperGrindIsJump = false;

                        if (CameraDragEnabled.Value && CameraLagBehaviour.Instance != null)
                        {
                            CameraLagBehaviour.Instance.TriggerLag();
                        }

                        if (MirrorBoostParticles.Value && charVis != null)
                            charVis.SetBoostpackEffect(BoostpackEffectMode.BOOSTING);

                        PlayBoostSfx(player, Instance.BoostLoopClip, true, 0.2f);

                        if (GetMemberValue(player, "ringParticles") is ParticleSystem ps)
                        {
                            Quaternion oldRot = ps.transform.localRotation;
                            ps.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);
                            ps.Emit(5);
                            ps.transform.localRotation = oldRot;
                        }
                    }
                }

                if (_activeHyperGrind)
                {
                    bool holdCurrent = _activeHyperGrindIsJump ? holdHyperJump : holdHyperBoost;

                    if (!holdCurrent || !isGrinding || isDead)
                    {
                        _activeHyperGrind = false;

                        if (MirrorBoostParticles.Value && charVis != null)
                            charVis.SetBoostpackEffect(BoostpackEffectMode.OFF);

                        StopBoostLoopSfx(player);
                        PlayBoostSfx(player, Instance.BoostStopClip, false, 1f);
                    }
                    else
                    {
                        float appliedSpeed = Mathf.Min(player.boostSpeed + BoostStartAmount.Value, BoostStartCap.Value);
                        player.SetSpeedFlat(appliedSpeed);
                    }
                }

                _wasHyperBoostingLastFrame = (currentAbility is HyperBoostAbility);
            }

            // Update HUD layout only when positions change
            if (_iconImage != null && _countText != null)
            {
                // Keep the existing rainbow colors and gray out an empty counter
                if (_currentCacheId <= 0 && !InfiniteHyperBoost.Value)
                {
                    _iconImage.color = Color.gray;
                    if (_overlayImage != null) _overlayImage.color = Color.gray;
                    _countText.color = Color.gray;
                }
                else
                {
                    float baseSpeed = 0.15f;
                    _iconImage.color = Color.HSVToRGB(Mathf.Repeat(Time.time * baseSpeed, 1f), 1f, 1f);
                    if (_overlayImage != null) _overlayImage.color = Color.white;
                    _countText.color = Color.HSVToRGB(Mathf.Repeat(Time.time * (baseSpeed * 0.17f), 1f), 1f, 1f);
                }

                Vector2 targetIconPos = new Vector2(IconPosX.Value, IconPosY.Value);
                if (_lastIconPos != targetIconPos)
                {
                    _iconRect.anchoredPosition = targetIconPos;
                    _lastIconPos = targetIconPos;
                }

                Vector2 targetTextOffset = new Vector2(TextOffsetX.Value, TextOffsetY.Value);
                if (_lastTextOffset != targetTextOffset)
                {
                    _textRect.anchoredPosition = targetTextOffset;
                    _lastTextOffset = targetTextOffset;
                }

                // Refresh the counter after config changes
                UpdateUIText();
            }

            if (!_fontSearchAttempted) HijackGameFont();
        }

        private void ConsumeHyperBoost(int amount)
        {
            if (!InfiniteHyperBoost.Value)
            {
                _currentCacheId -= amount;
                if (_currentCacheId < 0) _currentCacheId = 0;

                // Schedule a deferred save
                _isDirty = true;
                _saveTimer = SaveDelay;
            }
            UpdateUIText();
        }

        public void FlushSave()
        {
            if (!_isDirty) return;
            _isDirty = false;
            WriteCacheIdToFile(_currentCacheId);
        }

        private void ReadJsonData()
        {
            try
            {
                if (!File.Exists(_jsonPath)) return;
                using (FileStream fs = new FileStream(_jsonPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (StreamReader reader = new StreamReader(fs))
                {
                    string base64Data = reader.ReadToEnd();
                    if (string.IsNullOrWhiteSpace(base64Data)) return;

                    byte[] decodedBytes = Convert.FromBase64String(base64Data);
                    string jsonData = Encoding.UTF8.GetString(decodedBytes);

                    TelemetryCache cache = UnityEngine.JsonUtility.FromJson<TelemetryCache>(jsonData);
                    if (cache != null && cache.cache_id != _currentCacheId)
                    {
                        _currentCacheId = cache.cache_id;
                        UpdateUIText();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.LogWarning($"Failed to read or decode JSON: {ex.Message}");
            }
        }

        private void WriteCacheIdToFile(int newId)
        {
            try
            {
                if (_fileWatcher != null) _fileWatcher.EnableRaisingEvents = false;

                TelemetryCache cache = new TelemetryCache { cache_id = newId };
                string jsonData = UnityEngine.JsonUtility.ToJson(cache);
                byte[] encodedBytes = Encoding.UTF8.GetBytes(jsonData);
                string base64Data = Convert.ToBase64String(encodedBytes);

                File.WriteAllText(_jsonPath, base64Data);

                if (_fileWatcher != null) _fileWatcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                Log.LogError($"Failed to write JSON: {ex.Message}");
            }
        }

        private void SetupCustomHUD()
        {
            if (_hudCanvasObj != null) return;

            _hudCanvasObj = new GameObject("HyperBoostHUDCanvas");
            Canvas canvas = _hudCanvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10000;

            CanvasScaler scaler = _hudCanvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            // Icon setup
            GameObject iconObj = new GameObject("HyperBoostIcon");
            iconObj.transform.SetParent(_hudCanvasObj.transform, false);
            _iconImage = iconObj.AddComponent<Image>();

            _iconRect = iconObj.GetComponent<RectTransform>();
            _iconRect.anchorMin = new Vector2(0, 1);
            _iconRect.anchorMax = new Vector2(0, 1);
            _iconRect.pivot = new Vector2(0, 1);
            _iconRect.sizeDelta = new Vector2(64, 64);

            if (File.Exists(_iconPath))
            {
                byte[] fileData = File.ReadAllBytes(_iconPath);
                Texture2D tex = new Texture2D(64, 64);
                tex.LoadImage(fileData);
                Sprite sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                _iconImage.sprite = sprite;
            }

            // Overlay setup
            GameObject overlayObj = new GameObject("HyperBoostOverlay");
            overlayObj.transform.SetParent(iconObj.transform, false);
            _overlayImage = overlayObj.AddComponent<Image>();

            RectTransform overlayRect = overlayObj.GetComponent<RectTransform>();
            overlayRect.anchorMin = new Vector2(0, 0);
            overlayRect.anchorMax = new Vector2(1, 1);
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;

            if (File.Exists(_overlayPath))
            {
                byte[] overData = File.ReadAllBytes(_overlayPath);
                Texture2D overTex = new Texture2D(64, 64);
                overTex.LoadImage(overData);
                Sprite overSprite = Sprite.Create(overTex, new Rect(0, 0, overTex.width, overTex.height), new Vector2(0.5f, 0.5f));
                _overlayImage.sprite = overSprite;
            }

            // Counter text setup
            GameObject textObj = new GameObject("HyperBoostText");
            textObj.transform.SetParent(iconObj.transform, false);
            _countText = textObj.AddComponent<TextMeshProUGUI>();

            _countText.alignment = TextAlignmentOptions.Left;
            _countText.fontSize = 42;
            _countText.raycastTarget = false;

            _textRect = textObj.GetComponent<RectTransform>();
            _textRect.anchorMin = new Vector2(0, 0.5f);
            _textRect.anchorMax = new Vector2(0, 0.5f);
            _textRect.pivot = new Vector2(0, 0.5f);
            _textRect.sizeDelta = new Vector2(300, 100);

            UpdateUIText();
        }

        private void UpdateUIText()
        {
            if (_countText != null)
            {
                _countText.text = InfiniteHyperBoost.Value ? "= ‡" : $"= {FormatNumber(_currentCacheId)}";
            }
        }

        private string FormatNumber(int number)
        {
            if (!AbbreviateNumbers.Value) return number.ToString();

            if (number >= 1000000000)
                return (number / 1000000000D).ToString("0.#") + "B";

            if (number >= 1000000)
            {
                double mValue = number / 1000000D;
                return (mValue >= 100 ? mValue.ToString("0") : mValue.ToString("0.#")) + "M";
            }

            if (number >= 1000)
            {
                double kValue = number / 1000D;
                // Use whole-number formatting above 100K and one decimal below
                return (kValue >= 100 ? kValue.ToString("0") : kValue.ToString("0.#")) + "K";
            }

            return number.ToString();
        }

        private string GetFontStringName(HyperBoostFont font)
        {
            switch (font)
            {
                case HyperBoostFont._2070POLYESTER_ALP53: return "2070POLYESTER-ALP53";
                case HyperBoostFont.DFSoge5: return "DFSoge5";
                case HyperBoostFont.elements: return "elements";
                case HyperBoostFont.FerriteCoreDX_Black: return "FerriteCoreDX-Black";
                case HyperBoostFont.FORCED_SQUARE: return "FORCED SQUARE";
                case HyperBoostFont.forgotten_futurist_rg: return "forgotten futurist rg";
                case HyperBoostFont.GEIST_RND: return "GEIST_RND";
                case HyperBoostFont.induction: return "induction";
                case HyperBoostFont.KARNIVOB: return "KARNIVOB";
                case HyperBoostFont.KORATAKI_REGULAR: return "KORATAKI_REGULAR";
                case HyperBoostFont.ModernDOS8x16: return "ModernDOS8x16";
                case HyperBoostFont.Pitviper: return "Pitviper";
                case HyperBoostFont.Reality_Pursuit_NC: return "Reality Pursuit NC";
                case HyperBoostFont.V5PRD_ProphitDot: return "V5PRD_ProphitDot";
                default: return "2070POLYESTER-ALP53";
            }
        }

        private void HijackGameFont()
        {
            if (_fontSearchAttempted && _gameFont != null) return;

            _fontSearchTimer += Time.deltaTime;
            if (_fontSearchTimer < 2.0f) return;
            _fontSearchTimer = 0f;

            var allFonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            foreach (var font in allFonts)
            {
                if (font != null && !string.IsNullOrEmpty(font.name))
                {
                    if (!_availableFonts.ContainsKey(font.name))
                    {
                        _availableFonts[font.name] = font;
                    }
                    if (font.name.Contains("2070POLYESTER") && _polyesterFallbackFont == null)
                    {
                        _polyesterFallbackFont = font;
                    }
                }
            }

            string targetFontName = GetFontStringName(UIFont.Value);
            string sanitizedTarget = targetFontName.Replace(" ", "").Replace("_", "").Replace("-", "").ToLowerInvariant();
            TMP_FontAsset selectedFont = null;

            // Match font names after removing spaces, underscores, and hyphens
            foreach (var kvp in _availableFonts)
            {
                string sanitizedKey = kvp.Key.Replace(" ", "").Replace("_", "").Replace("-", "").ToLowerInvariant();
                if (sanitizedKey.StartsWith(sanitizedTarget))
                {
                    selectedFont = kvp.Value;
                    break;
                }
            }

            // Use 2070POLYESTER as the fallback font
            if (selectedFont == null && _polyesterFallbackFont != null)
            {
                selectedFont = _polyesterFallbackFont;
            }

            if (selectedFont != null && _countText != null)
            {
                // Destroy the previous font clone when changing fonts
                if (_gameFont != null && _gameFont.name.EndsWith("_HyperBoostClone"))
                {
                    Destroy(_gameFont);
                }

                // Clone the font to avoid changing the game's shared asset
                _gameFont = Instantiate(selectedFont);
                _gameFont.name = selectedFont.name + "_HyperBoostClone";

                // Remove the GEIST_RND outline using a cloned material
                if (UIFont.Value == HyperBoostFont.GEIST_RND && _gameFont.material != null)
                {
                    Material matClone = Instantiate(_gameFont.material);
                    if (matClone.HasProperty("_OutlineWidth"))
                    {
                        matClone.SetFloat("_OutlineWidth", 0f);
                    }
                    if (matClone.HasProperty("_OutlineSoftness"))
                    {
                        matClone.SetFloat("_OutlineSoftness", 0f);
                    }
                    _gameFont.material = matClone;
                }

                _countText.font = _gameFont;

                // Increase selected fonts by 20% for readability
                float baseSize = 42f;
                if (UIFont.Value == HyperBoostFont.DFSoge5 ||
                    UIFont.Value == HyperBoostFont.FORCED_SQUARE ||
                    UIFont.Value == HyperBoostFont.ModernDOS8x16)
                {
                    baseSize = 42f * 1.2f; // Target font size: 50.4
                }
                _countText.fontSize = baseSize;

                _fontSearchAttempted = true;
            }
        }

        private void OnDestroy()
        {
            FlushSave();

            if (_fileWatcher != null)
            {
                _fileWatcher.EnableRaisingEvents = false;
                _fileWatcher.Dispose();
            }
        }
    }

    // QTE Manager
    public class QTEManager : MonoBehaviour
    {
        public static QTEManager Instance;

        private GameObject qteRoot;
        private RectTransform shakeContainer;
        private AudioSource audioSource;
        private Coroutine _phoneUnlockCoroutine;

        private class QTESlot
        {
            public GameObject root;
            public Image shadow;
            public Image radialFill;
            public Image main;
            public Image ring;
            public int expectedActionId;
        }

        private QTESlot[] slots = new QTESlot[4];

        public bool IsQTEActive { get; private set; }
        private int currentSlotIndex = 0;
        private float slotTimer = 0f;
        private const float SLOT_TIME = 0.99f;

        private Action onQTESuccess;
        private Action onQTEFail;

        private int[] dPads = new int[] { 21, 29, 56, 57 };
        private int[] buttons = new int[] { 12, 65, 7, 15 };
        private List<int> currentPool;

        void Awake()
        {
            Instance = this;
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.spatialBlend = 0f;
            audioSource.volume = 0.8f;
            CreateQTECanvas();
        }

        private void ReleasePhoneWithDelay(float delay = 0.75f)
        {
            if (_phoneUnlockCoroutine != null)
            {
                StopCoroutine(_phoneUnlockCoroutine);
            }
            _phoneUnlockCoroutine = StartCoroutine(UnlockPhoneRoutine(delay));
        }

        private IEnumerator UnlockPhoneRoutine(float delay)
        {
            Player p = WorldHandler.instance?.GetCurrentPlayer();
            if (p != null)
            {
                Plugin.SetMemberValue(p, "phoneLocked", true);
            }
            yield return new WaitForSeconds(delay);
            p = WorldHandler.instance?.GetCurrentPlayer();
            if (p != null)
            {
                Plugin.SetMemberValue(p, "phoneLocked", false);
            }
            _phoneUnlockCoroutine = null;
        }

        private void CreateQTECanvas()
        {
            GameObject canvasObj = new GameObject("QTECanvas");
            canvasObj.transform.SetParent(transform);
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10001; // Render above the HUD

            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            qteRoot = new GameObject("QTERoot");
            qteRoot.transform.SetParent(canvasObj.transform, false);
            shakeContainer = qteRoot.AddComponent<RectTransform>();
            shakeContainer.anchorMin = new Vector2(0.5f, 0.5f);
            shakeContainer.anchorMax = new Vector2(0.5f, 0.5f);
            shakeContainer.sizeDelta = new Vector2(800, 200);

            // Place slots 250 px apart: 200 px wide with a 50 px gap
            float startX = -375f;
            for (int i = 0; i < 4; i++)
            {
                slots[i] = CreateSlot(shakeContainer, new Vector2(startX + (i * 250f), 0));
            }

            qteRoot.SetActive(false);
        }

        private QTESlot CreateSlot(Transform parent, Vector2 pos)
        {
            QTESlot slot = new QTESlot();
            slot.root = new GameObject("Slot");
            RectTransform rect = slot.root.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchoredPosition = pos;
            rect.sizeDelta = new Vector2(200, 200);

            // Shadow
            GameObject shadowObj = new GameObject("Shadow");
            shadowObj.transform.SetParent(rect, false);
            slot.shadow = shadowObj.AddComponent<Image>();
            slot.shadow.rectTransform.sizeDelta = new Vector2(240, 240); // 1.2x slot size
            slot.shadow.color = new Color(0.2f, 0.2f, 0.2f, 1f);

            // Radial timer
            GameObject radialObj = new GameObject("RadialFill");
            radialObj.transform.SetParent(rect, false);
            slot.radialFill = radialObj.AddComponent<Image>();
            slot.radialFill.rectTransform.sizeDelta = new Vector2(240, 240);
            slot.radialFill.color = new Color(1f, 1f, 1f, 0.5f);
            slot.radialFill.type = Image.Type.Filled;
            slot.radialFill.fillMethod = Image.FillMethod.Radial360;
            slot.radialFill.fillOrigin = (int)Image.Origin360.Top;
            slot.radialFill.fillClockwise = true;
            slot.radialFill.fillAmount = 0f;

            // Expanding ring
            GameObject ringObj = new GameObject("Ring");
            ringObj.transform.SetParent(rect, false);
            slot.ring = ringObj.AddComponent<Image>();
            slot.ring.rectTransform.sizeDelta = new Vector2(640, 640);
            slot.ring.gameObject.SetActive(false);
            if (Plugin.Instance.QteCircleSprite != null) slot.ring.sprite = Plugin.Instance.QteCircleSprite;

            // Main button
            GameObject mainObj = new GameObject("Main");
            mainObj.transform.SetParent(rect, false);
            slot.main = mainObj.AddComponent<Image>();
            slot.main.rectTransform.sizeDelta = new Vector2(200, 200);

            return slot;
        }

        public void StartQTE(bool isHyperJump, Action successCallback, Action failCallback)
        {
            if (IsQTEActive) return;

            // Route QTE sounds through the game's SFX mixer
            if (audioSource.outputAudioMixerGroup == null && Reptile.Core.Instance != null && Reptile.Core.Instance.gameplaySfxAudioSource != null)
            {
                audioSource.outputAudioMixerGroup = Reptile.Core.Instance.gameplaySfxAudioSource.outputAudioMixerGroup;
            }

            if (_phoneUnlockCoroutine != null)
            {
                StopCoroutine(_phoneUnlockCoroutine);
                _phoneUnlockCoroutine = null;
            }

            // Lock the phone without disabling Rewired input
            Player p = WorldHandler.instance?.GetCurrentPlayer();
            if (p != null)
            {
                Plugin.SetMemberValue(p, "phoneLocked", true);
            }

            onQTESuccess = successCallback;
            onQTEFail = failCallback;

            currentPool = new List<int>(dPads);
            if (!isHyperJump) currentPool.AddRange(buttons);

            for (int i = 0; i < 4; i++)
            {
                int randomAction = currentPool[UnityEngine.Random.Range(0, currentPool.Count)];
                slots[i].expectedActionId = randomAction;

                Sprite spr = Plugin.Instance.QteSprites.ContainsKey(randomAction) ? Plugin.Instance.QteSprites[randomAction] : null;
                slots[i].main.sprite = spr;
                slots[i].shadow.sprite = spr;
                slots[i].radialFill.sprite = spr;

                slots[i].main.color = Color.white;
                slots[i].shadow.color = new Color(0.2f, 0.2f, 0.2f, 1f);
                slots[i].radialFill.fillAmount = 0f;
                slots[i].radialFill.gameObject.SetActive(true);

                slots[i].main.rectTransform.localScale = Vector3.one;
                slots[i].shadow.rectTransform.localScale = Vector3.one; // Already sized to 240 px
            }

            shakeContainer.anchoredPosition = Vector2.zero;
            currentSlotIndex = 0;
            slotTimer = 0f;
            IsQTEActive = true;
            qteRoot.SetActive(true);
        }

        void Update()
        {
            if (!IsQTEActive) return;

            slotTimer += Time.deltaTime;
            float progress = slotTimer / SLOT_TIME;
            slots[currentSlotIndex].radialFill.fillAmount = progress;

            if (slotTimer >= SLOT_TIME)
            {
                slots[currentSlotIndex].radialFill.fillAmount = 1f;
                FailQTE();
                return;
            }

            if (Core.Instance != null && Core.Instance.GameInput != null)
            {
                GameInput gi = Core.Instance.GameInput;

                // Read native inputs directly without disabling input maps
                int expected = slots[currentSlotIndex].expectedActionId;
                bool pressedExpected = gi.GetButtonNew(expected);

                bool pressedWrong = false;
                foreach (int action in currentPool)
                {
                    if (action != expected && gi.GetButtonNew(action))
                    {
                        pressedWrong = true;
                        break;
                    }
                }

                if (pressedWrong)
                {
                    FailQTE();
                }
                else if (pressedExpected)
                {
                    AdvanceSlot();
                }
            }
        }

        private void AdvanceSlot()
        {
            if (Plugin.Instance.QteOkayClip != null) audioSource.PlayOneShot(Plugin.Instance.QteOkayClip);

            StartCoroutine(JoltSlot(slots[currentSlotIndex]));
            StartCoroutine(AnimateRing(slots[currentSlotIndex]));

            slots[currentSlotIndex].radialFill.gameObject.SetActive(false);

            currentSlotIndex++;
            slotTimer = 0f;

            if (currentSlotIndex >= 4)
            {
                CompleteQTE();
            }
        }

        private void CompleteQTE()
        {
            IsQTEActive = false;

            // Keep the phone locked for 0.75 seconds after QTE completion
            ReleasePhoneWithDelay(0.75f);

            if (Plugin.Instance.QteCompleteClip != null) audioSource.PlayOneShot(Plugin.Instance.QteCompleteClip);
            StartCoroutine(ShakeRoutine(0.3f, () => {
                qteRoot.SetActive(false);
                onQTESuccess?.Invoke();
            }));
        }

        private void FailQTE()
        {
            IsQTEActive = false;

            // Keep the phone locked for 0.75 seconds after QTE failure to absorb trailing input
            ReleasePhoneWithDelay(0.75f);

            if (Plugin.Instance.QteWrongClip != null) audioSource.PlayOneShot(Plugin.Instance.QteWrongClip);

            for (int i = 0; i < 4; i++)
            {
                // Tint all QTE slot layers
                slots[i].main.color = new Color(1f, 0.5f, 0.5f, 1f);
                slots[i].shadow.color = new Color(0.8f, 0.2f, 0.2f, 1f); // Dark red shadow
                slots[i].radialFill.color = new Color(1f, 0.5f, 0.5f, 0.5f); // Translucent red timer tint
            }

            StartCoroutine(ShakeRoutine(0.3f, () => {
                qteRoot.SetActive(false);
                onQTEFail?.Invoke();
            }));
        }

        public void CancelQTE()
        {
            if (!IsQTEActive) return;
            IsQTEActive = false;

            // Keep the phone locked for 0.75 seconds after QTE cancellation
            ReleasePhoneWithDelay(0.75f);

            qteRoot.SetActive(false);
        }

        private IEnumerator JoltSlot(QTESlot slot)
        {
            float timer = 0f;
            float duration = 0.15f;

            while (timer < duration)
            {
                timer += Time.deltaTime;
                float t = timer / duration;
                float scale = Mathf.Lerp(1.2f, 1.0f, t);
                slot.main.rectTransform.localScale = new Vector3(scale, scale, 1f);
                slot.shadow.rectTransform.localScale = new Vector3(scale, scale, 1f);
                yield return null;
            }
            slot.main.rectTransform.localScale = Vector3.one;
            slot.shadow.rectTransform.localScale = Vector3.one;
        }

        private IEnumerator AnimateRing(QTESlot slot)
        {
            slot.ring.gameObject.SetActive(true);
            float timer = 0f;
            float duration = 0.3f;

            while (timer < duration)
            {
                timer += Time.deltaTime;
                float t = timer / duration;

                // Scale the ring from 0.1 to 1.0; its 640 px rect is 3.2x the 200 px slot
                float currentScale = Mathf.Lerp(0.1f, 1.0f, t);
                slot.ring.rectTransform.localScale = new Vector3(currentScale, currentScale, 1f);

                Color c = slot.ring.color;
                c.a = Mathf.Lerp(1f, 0f, t);
                slot.ring.color = c;

                yield return null;
            }
            slot.ring.gameObject.SetActive(false);
        }

        private IEnumerator ShakeRoutine(float duration, Action onComplete)
        {
            float timer = 0f;
            Vector2 startPos = shakeContainer.anchoredPosition;

            while (timer < duration)
            {
                timer += Time.deltaTime;
                shakeContainer.anchoredPosition = startPos + (UnityEngine.Random.insideUnitCircle * 15f);
                yield return null;
            }

            shakeContainer.anchoredPosition = startPos;
            onComplete?.Invoke();
        }
    }

    // Harmony patches

    [HarmonyPatch(typeof(Reptile.GameplayCamera), "Awake")]
    class GameplayCameraAwakePatch
    {
        static void Postfix(Reptile.GameplayCamera __instance)
        {
            if (__instance.gameObject.GetComponent<CameraLagBehaviour>() == null)
            {
                __instance.gameObject.AddComponent<CameraLagBehaviour>();
            }
        }
    }

    // Block native boost tricks while the QTE is active
    [HarmonyPatch(typeof(Reptile.Player), "CheckBoostTrick")]
    class PlayerCheckBoostTrickPatch
    {
        static bool Prefix(ref bool __result)
        {
            if (QTEManager.Instance != null && QTEManager.Instance.IsQTEActive)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Reptile.GrindAbility), "CheckBoostTrick")]
    class GrindAbilityCheckBoostTrickPatch
    {
        static bool Prefix(ref bool __result)
        {
            if (QTEManager.Instance != null && QTEManager.Instance.IsQTEActive)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    // Camera drag

    public class CameraLagBehaviour : MonoBehaviour
    {
        public static CameraLagBehaviour Instance;
        private bool isLagging = false;
        private float lagTimer = 0f;
        private Vector3 lagStartCameraPos;
        private Quaternion lagStartCameraRot;

        // Audio listener restoration
        private AudioListener _nativeListener;
        private GameObject _frozenListenerObj;
        private bool _isRestoringAudio = false;
        private float _audioRestoreTimer = 0f;
        private const float AudioRestoreDuration = 0.5f;

        void Awake()
        {
            Instance = this;
        }

        void LateUpdate()
        {
            // Move the frozen listener back to the camera
            if (_isRestoringAudio && _frozenListenerObj != null)
            {
                _audioRestoreTimer += Time.deltaTime;
                float t = Mathf.Clamp01(_audioRestoreTimer / AudioRestoreDuration);

                // Blend the frozen listener back to the camera
                _frozenListenerObj.transform.position = Vector3.Lerp(_frozenListenerObj.transform.position, transform.position, t);
                _frozenListenerObj.transform.rotation = Quaternion.Slerp(_frozenListenerObj.transform.rotation, transform.rotation, t);

                if (t >= 1f)
                {
                    RestoreNativeListener();
                }
            }

            if (Plugin.Instance == null || !Plugin.Instance.IsEnabled.Value || !Plugin.Instance.CameraDragEnabled.Value)
            {
                if (isLagging) EndLagAndRestoreAudio();
                return;
            }

            if (isLagging)
            {
                lagTimer += Time.deltaTime;
                float duration = Plugin.Instance.CameraDragDuration.Value;
                float lockDuration = duration + 0.15f; // Keep rotation locked briefly after the drag to prevent snapping

                if (lagTimer >= lockDuration || duration <= 0f)
                {
                    EndLagAndRestoreAudio();
                }
                else
                {
                    // Ignore camera input during the drag and its lock buffer
                    Player player = WorldHandler.instance?.GetCurrentPlayer();
                    if (player != null)
                    {
                        Plugin.SetMemberValue(player, "cameraInputBuffer", Vector2.zero);
                    }

                    // Apply camera drag only during the configured duration
                    if (lagTimer < duration)
                    {
                        // Save the camera's intended position and rotation
                        Vector3 targetPos = transform.position;
                        Quaternion targetRot = transform.rotation;

                        float staticDuration = duration * 0.30f;
                        float catchupDuration = duration - staticDuration;

                        // Hold the camera still for the first 30% of the drag
                        if (lagTimer < staticDuration)
                        {
                            transform.position = lagStartCameraPos;
                            transform.rotation = lagStartCameraRot;
                        }
                        // Blend back to the target for the rest of the drag
                        else
                        {
                            float t = (lagTimer - staticDuration) / catchupDuration;
                            t = Mathf.SmoothStep(0f, 1f, t);

                            Plugin.CameraDragStyle dragStyle = Plugin.Instance.CameraDragStyleConfig.Value;

                            Vector3 lerpPos = Vector3.Lerp(lagStartCameraPos, targetPos, t);
                            Vector3 slerpPos = Vector3.Slerp(lagStartCameraPos, targetPos, t);

                            Quaternion lerpRot = Quaternion.Lerp(lagStartCameraRot, targetRot, t);
                            Quaternion slerpRot = Quaternion.Slerp(lagStartCameraRot, targetRot, t);

                            if (dragStyle == Plugin.CameraDragStyle.Lerp)
                            {
                                transform.position = lerpPos;
                                transform.rotation = lerpRot;
                            }
                            else if (dragStyle == Plugin.CameraDragStyle.Slerp)
                            {
                                transform.position = slerpPos;
                                transform.rotation = slerpRot;
                            }
                            else // Mixed
                            {
                                // Measure the camera rotation change
                                // Use Lerp for small turns and Slerp for larger turns
                                float angleChange = Quaternion.Angle(lagStartCameraRot, targetRot);

                                // Blend from Lerp at 10 degrees to Slerp at 45 degrees
                                float blendFactor = Mathf.Clamp01((angleChange - 10f) / 35f);

                                transform.position = Vector3.Lerp(lerpPos, slerpPos, blendFactor);
                                transform.rotation = Quaternion.Lerp(lerpRot, slerpRot, blendFactor);
                            }
                        }
                    }
                }
            }
        }

        public void TriggerLag()
        {
            isLagging = true;
            lagTimer = 0f;
            lagStartCameraPos = transform.position;
            lagStartCameraRot = transform.rotation;

            // Freeze the audio listener at the drag start
            if (!_isRestoringAudio) // Avoid duplicate listeners during rapid triggers
            {
                _nativeListener = gameObject.GetComponentInChildren<AudioListener>();
                if (_nativeListener != null)
                {
                    _nativeListener.enabled = false;
                    _frozenListenerObj = new GameObject("HyperBoost_FrozenListener");
                    _frozenListenerObj.transform.position = lagStartCameraPos;
                    _frozenListenerObj.transform.rotation = lagStartCameraRot;
                    AudioListener frozenListener = _frozenListenerObj.AddComponent<AudioListener>();

                    // Audio listener fix
                    // Register the new listener with BRC's AudioManager
                    Plugin.SetActiveAudioListenerSafe(frozenListener);
                }
            }
            else if (_frozenListenerObj != null)
            {
                // Reuse the frozen listener if another drag starts during restoration
                _isRestoringAudio = false;
                _frozenListenerObj.transform.position = lagStartCameraPos;
                _frozenListenerObj.transform.rotation = lagStartCameraRot;
            }
        }

        private void EndLagAndRestoreAudio()
        {
            if (!isLagging) return;
            isLagging = false;

            // Start restoring the audio listener
            if (_frozenListenerObj != null)
            {
                _isRestoringAudio = true;
                _audioRestoreTimer = 0f;
            }
            else
            {
                RestoreNativeListener();
            }
        }

        private void RestoreNativeListener()
        {
            _isRestoringAudio = false;
            if (_nativeListener != null)
            {
                _nativeListener.enabled = true;

                // Audio listener fix
                // Restore the native listener in BRC's AudioManager
                Plugin.SetActiveAudioListenerSafe(_nativeListener);
            }
            if (_frozenListenerObj != null)
            {
                Destroy(_frozenListenerObj);
                _frozenListenerObj = null;
            }
        }

        private void OnDisable()
        {
            isLagging = false;
            RestoreNativeListener();
        }

        private void OnDestroy()
        {
            RestoreNativeListener();
        }
    }

    // Custom player ability
    public class HyperBoostAbility : Ability
    {
        public bool isHyperJump = false;
        public bool haveAirStartBoost = true; // Ability compatibility handling
        private bool isWaitingForQTE = false;
        private bool _isAborted = false;

        public enum State
        {
            START_BOOST,
            BOOST,
            BRAKE
        }

        private State state;
        private float stateTimer;
        private bool inAir;
        private bool _hitboxesActive = false; // Track hitbox state to avoid redundant SetActive calls

        // Blend vertical speed from its value at charge start
        // Keep grounded movement locked during charging
        private float _chargingInitialVerticalSpeed = 0f;
        private float _chargingVerticalBlendTimer = 0f;
        private bool _chargingWasGrounded = false;

        private RuntimeAnimatorController _originalController;
        private Animator _abilityAnimator;

        private int startBoostHash = Animator.StringToHash("startBoost");
        private int boostBrakeHash = Animator.StringToHash("boostBrake");
        private int fallHash = Animator.StringToHash("fall");
        private int jumpHash = Animator.StringToHash("jump");

        public HyperBoostAbility(Player player) : base(player) { }

        public override void Init()
        {
            allowNormalJump = true;
            canStartGrind = true;
            haveAirStartBoost = Plugin.Instance.ChargingDelayTime.Value > 0f;
            _isAborted = false;
        }

        public override void OnStartAbility()
        {
            if (Plugin.Instance.QteModeEnabled.Value)
            {
                isWaitingForQTE = true;
                allowNormalJump = false; // Prevent jump input from carrying through the QTE
                SetState(State.START_BOOST);

                QTEManager.Instance.StartQTE(this.isHyperJump,
                    successCallback: () => {
                        if (this._isAborted) return;
                        isWaitingForQTE = false;

                        // Recheck held inputs after the QTE success delay
                        bool isHeld = false;
                        if (Core.Instance != null && Core.Instance.GameInput != null)
                        {
                            if (this.isHyperJump)
                            {
                                isHeld = Core.Instance.GameInput.GetButtonHeld(15) &&
                                         Core.Instance.GameInput.GetButtonHeld(12) &&
                                         Core.Instance.GameInput.GetButtonHeld(65);
                            }
                            else
                            {
                                bool boostH = Core.Instance.GameInput.GetButtonHeld(18);
                                isHeld = boostH && Core.Instance.GameInput.GetButtonHeld(8);
                            }
                        }

                        if (isHeld)
                        {
                            SetState(State.BOOST);
                        }
                        else
                        {
                            p.StopCurrentAbility();
                        }
                    },
                    failCallback: () => {
                        if (this._isAborted) return;
                        isWaitingForQTE = false;
                        p.StopCurrentAbility();
                    }
                );
            }
            else if (Plugin.Instance.ChargingDelayTime.Value <= 0f)
            {
                // Skip the delay while preserving START_BOOST initialization
                p.ResetLean();
                inAir = !p.IsGrounded();
                Plugin.SetMemberValue(p, "boosting", false);
                SetState(State.BOOST);
            }
            else
            {
                SetState(State.START_BOOST);
            }
        }

        private void SetHitboxesActive(bool active)
        {
            if (_hitboxesActive == active) return;
            _hitboxesActive = active;

            try
            {
                if (p == null) return;

                string[] hitboxNames = new string[] { "hitbox", "hitboxLeftLeg", "hitboxRightLeg", "hitboxUpperBody", "radialHitbox" };
                foreach (string hbName in hitboxNames)
                {
                    GameObject hb = Plugin.GetMemberValue(p, hbName) as GameObject;
                    if (hb != null && hb.activeSelf != active)
                    {
                        hb.SetActive(active);
                    }
                }
            }
            catch (Exception e)
            {
                if (Plugin.Log != null) Plugin.Log.LogWarning($"[HyperBoostMod] Failed to toggle hitboxes: {e.Message}");
            }
        }

        private string GetAnimString(bool isGround, bool isCharging = false)
        {
            MoveStyle ms = MoveStyle.ON_FOOT;
            object msObj = Plugin.GetMemberValue(p, "moveStyle");
            if (msObj is MoveStyle) ms = (MoveStyle)msObj;

            string msStr = ms.ToString();

            if (isCharging)
            {
                if (msStr.Contains("BMX")) return Plugin.Instance.BmxChargingAnim.Value;
                if (msStr.Contains("SKATEBOARD")) return Plugin.Instance.SkateboardChargingAnim.Value;
                if (msStr.Contains("INLINE") || msStr.Contains("SKATES")) return Plugin.Instance.InlineChargingAnim.Value;
                return Plugin.Instance.OnFootChargingAnim.Value;
            }

            if (isGround)
            {
                if (msStr.Contains("BMX")) return Plugin.Instance.BmxGroundAnim.Value;
                if (msStr.Contains("SKATEBOARD")) return Plugin.Instance.SkateboardGroundAnim.Value;
                if (msStr.Contains("INLINE") || msStr.Contains("SKATES")) return Plugin.Instance.InlineGroundAnim.Value;
                return Plugin.Instance.OnFootGroundAnim.Value;
            }
            else
            {
                if (msStr.Contains("BMX")) return Plugin.Instance.BmxAirAnim.Value;
                if (msStr.Contains("SKATEBOARD")) return Plugin.Instance.SkateboardAirAnim.Value;
                if (msStr.Contains("INLINE") || msStr.Contains("SKATES")) return Plugin.Instance.InlineAirAnim.Value;
                return Plugin.Instance.OnFootAirAnim.Value;
            }
        }

        private RuntimeAnimatorController GetMovestyleController(Player p)
        {
            MoveStyle currentStyle = MoveStyle.ON_FOOT;
            object moveStyleObj = Plugin.GetMemberValue(p, "moveStyle");
            if (moveStyleObj is MoveStyle ms) currentStyle = ms;

            string fieldName = "animatorController";
            string msStr = currentStyle.ToString();

            if (msStr.Contains("BMX")) fieldName = "animatorControllerBMX";
            else if (msStr.Contains("SKATEBOARD")) fieldName = "animatorControllerSkateboard";
            else if (msStr.Contains("INLINE") || msStr.Contains("SKATES")) fieldName = "animatorControllerSkates";

            return Plugin.GetMemberValue(p, fieldName) as RuntimeAnimatorController;
        }

        private void PlayMovestyleAnimation(string animStr)
        {
            CharacterVisual charVis = Plugin.GetCharacterVisual(p);
            if (charVis == null) return;

            _abilityAnimator = (Animator)Plugin.GetMemberValue(charVis, "anim");
            if (_abilityAnimator == null) return;

            RuntimeAnimatorController movestyleController = GetMovestyleController(p);

            int targetHash;
            bool isBoeAnim = BunchOfEmotesSupport.TryGetGameAnimationForCustomAnimation(animStr, out targetHash);

            if (!isBoeAnim)
            {
                targetHash = Animator.StringToHash(animStr);
            }

            RuntimeAnimatorController finalController = isBoeAnim ? BunchOfEmotesSupport.GetController() : movestyleController;

            if (finalController != null && _abilityAnimator.runtimeAnimatorController != finalController)
            {
                if (_originalController == null) _originalController = _abilityAnimator.runtimeAnimatorController;
                Plugin.SwapAnimatorControllerSafely(_abilityAnimator, finalController);
            }

            p.PlayAnim(targetHash);
        }

        public void SetState(State setState)
        {
            bool wasBoosting = (state == State.BOOST);
            stateTimer = 0f;
            state = setState;

            CharacterVisual charVis = Plugin.GetCharacterVisual(p);

            switch (state)
            {
                case State.START_BOOST:
                    canStartGrind = false; // Block grind interactions during startup
                    SetHitboxesActive(false);
                    p.ResetLean();
                    inAir = !p.IsGrounded();

                    // Start the vertical-speed blend from the current speed
                    // Avoid snapping to the configured value
                    _chargingInitialVerticalSpeed = p.motor.velocity.y;
                    _chargingVerticalBlendTimer = 0f;
                    _chargingWasGrounded = !inAir;

                    Plugin.SetMemberValue(p, "boosting", false);

                    if (this.isHyperJump)
                    {
                        p.PlayAnim(startBoostHash);
                        normalMovement = false;
                        customVelocity = Vector3.zero;
                        targetSpeed = 0f;
                    }
                    else
                    {
                        string startAnimStr = GetAnimString(p.IsGrounded(), true);
                        PlayMovestyleAnimation(startAnimStr);

                        normalMovement = true;
                        p.SetSpeedFlat(0f); // Keep the player in place during startup
                        targetSpeed = 0f;   // Stop forward movement during charging
                        rotateSpeed = 3f;
                        decc = 10f;
                    }
                    break;

                case State.BOOST:
                    // Trigger camera drag when boost starts
                    if (!wasBoosting && Plugin.Instance != null && Plugin.Instance.CameraDragEnabled.Value && CameraLagBehaviour.Instance != null)
                    {
                        CameraLagBehaviour.Instance.TriggerLag();
                    }

                    if (this.isHyperJump)
                    {
                        canStartGrind = false; // Keep false until FixedUpdateAbility enables grinding
                    }
                    else
                    {
                        canStartGrind = true;
                    }

                    allowNormalJump = true; // Restore jumping after the QTE
                    SetHitboxesActive(true);
                    Plugin.SetMemberValue(p, "boosting", true);

                    // Play the start and loop SFX from the custom WAV assets
                    Plugin.PlayBoostSfx(p, Plugin.Instance.BoostStartClip, false, 1f);
                    Plugin.PlayBoostSfx(p, Plugin.Instance.BoostLoopClip, true, 0.6f);

                    if (Plugin.Instance.MirrorBoostParticles.Value && charVis != null)
                    {
                        charVis.SetBoostpackEffect(BoostpackEffectMode.BOOSTING);
                    }

                    if (this.isHyperJump)
                    {
                        normalMovement = true;

                        Plugin.SetMemberValue(p, "isJumping", true);
                        Plugin.SetMemberValue(p, "jumpRequested", true);
                        Plugin.SetMemberValue(p, "jumpConsumed", true);
                        Plugin.SetMemberValue(p, "jumpedThisFrame", true);

                        p.motor.ForceUnground();
                        p.motor.velocity = new Vector3(0f, Plugin.Instance.HyperJumpSpeed.Value, 0f);

                        inAir = true;
                        p.PlayAnim(jumpHash);

                        Plugin.PlayVoiceSafe(p, "VoiceJump");

                        // Trigger the game's jump effects
                        Plugin.InvokeMethodSafe(p, "DoJumpEffects", new object[] { Vector3.down });
                        Plugin.InvokeMethodSafe(p, "DoHighJumpEffects", new object[] { Vector3.down });
                    }
                    else
                    {
                        normalMovement = true;

                        string animStr = GetAnimString(p.IsGrounded(), false);
                        PlayMovestyleAnimation(animStr);

                        float appliedSpeed = Mathf.Min(p.boostSpeed + Plugin.Instance.BoostStartAmount.Value, Plugin.Instance.BoostStartCap.Value);
                        targetSpeed = appliedSpeed;
                        p.SetSpeedFlat(appliedSpeed);
                        rotateSpeed = 0.9f;

                        if (Plugin.GetMemberValue(p, "ringParticles") is ParticleSystem ps2)
                        {
                            Quaternion oldRot = ps2.transform.localRotation;
                            ps2.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);
                            ps2.Emit(5);
                            ps2.transform.localRotation = oldRot;
                        }
                    }
                    break;

                case State.BRAKE:
                    canStartGrind = true;
                    SetHitboxesActive(false);
                    RestoreAnimator();
                    Plugin.SetMemberValue(p, "boosting", false);

                    Plugin.StopBoostLoopSfx(p);
                    Plugin.PlayBoostSfx(p, Plugin.Instance.BoostStopClip, false, 1f);

                    if (Plugin.Instance.MirrorBoostParticles.Value && charVis != null)
                    {
                        charVis.SetBoostpackEffect(BoostpackEffectMode.OFF);
                    }

                    p.PlayAnim(boostBrakeHash);
                    rotateSpeed = 0.5f;
                    targetSpeed = 0f;
                    decc = 13f;
                    break;
            }
        }

        private float GetBlendedChargingVerticalSpeed()
        {
            float targetVerticalSpeed = -Plugin.Instance.ChargingFallSpeed.Value;
            float blendDuration = Mathf.Max(0f, Plugin.Instance.ChargingFallBlendTime.Value);

            // A zero blend duration applies the target speed immediately
            if (blendDuration <= 0.0001f)
                return targetVerticalSpeed;

            _chargingVerticalBlendTimer += Mathf.Max(0f, Core.dt);
            float blendProgress = Mathf.Clamp01(_chargingVerticalBlendTimer / blendDuration);

            // SmoothStep eases in and out of the speed transition
            blendProgress = blendProgress * blendProgress * (3f - 2f * blendProgress);
            return Mathf.Lerp(_chargingInitialVerticalSpeed, targetVerticalSpeed, blendProgress);
        }

        public override void FixedUpdateAbility()
        {
            p.OrientVisualToSurfaceInAbility();
            stateTimer += Core.dt;

            // Native boost trick interception
            // Allow tricks to interrupt only when the QTE is inactive
            if (!this.isHyperJump && !isWaitingForQTE)
            {
                // Read internal abilities through the reflection cache
                Ability groundTrickAbility = Plugin.GetMemberValue(p, "groundTrickAbility") as Ability;
                Ability airDashAbility = Plugin.GetMemberValue(p, "airDashAbility") as Ability;
                Ability vertAbility = Plugin.GetMemberValue(p, "vertAbility") as Ability;
                Ability airTrickAbility = Plugin.GetMemberValue(p, "airTrickAbility") as Ability;

                bool trickActivated = (groundTrickAbility != null && groundTrickAbility.CheckActivation()) ||
                                      (airDashAbility != null && airDashAbility.CheckActivation()) ||
                                      (vertAbility != null && vertAbility.CheckActivation()) ||
                                      (airTrickAbility != null && airTrickAbility.CheckActivation());

                if (trickActivated)
                {
                    if (Plugin.Instance != null) Plugin.Instance.ResumeHyperBoostAfterTrick = true;
                    return;
                }
            }
            // End boost trick interception

            if (state == State.START_BOOST)
            {
                // Recheck grounding each physics tick to catch landings during charging
                // Catch landings during longer charge delays
                bool isGroundedNow = p.IsGrounded();
                inAir = !isGroundedNow;

                if (isGroundedNow)
                {
                    // Keep the player at the landing point until charging ends
                    // Disable normal movement to stop MoveDrive from adding forward speed
                    // customVelocity also keeps the grounded lock active in Player.Move()
                    normalMovement = false;
                    customVelocity = Vector3.zero;
                    p.motor.velocity = Vector3.zero;
                    _chargingWasGrounded = true;
                }
                else
                {
                    // If the player becomes airborne during charging, restart the blend
                    // Restart the blend from the current vertical speed
                    if (_chargingWasGrounded)
                    {
                        _chargingInitialVerticalSpeed = p.motor.velocity.y;
                        _chargingVerticalBlendTimer = 0f;
                        _chargingWasGrounded = false;
                    }

                    float chargingVerticalSpeed = GetBlendedChargingVerticalSpeed();

                    if (this.isHyperJump)
                    {
                        // HyperJump uses customVelocity with normalMovement disabled
                        customVelocity = new Vector3(0f, chargingVerticalSpeed, 0f);
                        p.motor.velocity = customVelocity;
                    }
                    else
                    {
                        // Preserve horizontal air movement and blend only vertical speed
                        normalMovement = true;
                        Vector3 currentVelocity = p.motor.velocity;
                        p.motor.velocity = new Vector3(currentVelocity.x, chargingVerticalSpeed, currentVelocity.z);
                    }
                }

                // Check whether the required inputs are still held
                bool isHeld = false;
                if (Core.Instance != null && Core.Instance.GameInput != null)
                {
                    if (this.isHyperJump)
                    {
                        isHeld = Core.Instance.GameInput.GetButtonHeld(15) &&
                                 Core.Instance.GameInput.GetButtonHeld(12) &&
                                 Core.Instance.GameInput.GetButtonHeld(65);
                    }
                    else
                    {
                        bool boostH = Core.Instance.GameInput.GetButtonHeld(18);
                        isHeld = boostH && Core.Instance.GameInput.GetButtonHeld(8);
                    }
                }

                if (isWaitingForQTE)
                {
                    if (!isHeld)
                    {
                        QTEManager.Instance.CancelQTE();
                        p.StopCurrentAbility();
                    }
                    return; // Wait for the QTE before advancing the state timer
                }

                if (stateTimer >= Plugin.Instance.ChargingDelayTime.Value)
                {
                    SetState(State.BOOST);
                }
            }
            else if (state == State.BOOST)
            {
                // Allow grinding 0.2 seconds after HyperJump starts
                if (this.isHyperJump)
                {
                    if (stateTimer >= 0.2f)
                    {
                        canStartGrind = true;
                    }
                }

                if (this.isHyperJump)
                {
                    if (inAir)
                    {
                        int curAnim = Plugin.GetMemberValue(p, "curAnim") is int ca ? ca : 0;
                        if (p.motor.velocity.y <= 0f)
                        {
                            SetHitboxesActive(false); // Disable hitboxes when upward momentum ends
                            if (curAnim == jumpHash)
                            {
                                p.PlayAnim(fallHash);
                            }
                        }
                    }
                }

                bool isHeld = false;
                if (Core.Instance != null && Core.Instance.GameInput != null)
                {
                    if (this.isHyperJump)
                    {
                        isHeld = Core.Instance.GameInput.GetButtonHeld(15) &&
                                 Core.Instance.GameInput.GetButtonHeld(12) &&
                                 Core.Instance.GameInput.GetButtonHeld(65);
                    }
                    else
                    {
                        bool boostH = Core.Instance.GameInput.GetButtonHeld(18);
                        isHeld = boostH && Core.Instance.GameInput.GetButtonHeld(8);
                    }
                }

                if (!isHeld)
                {
                    if (p.IsGrounded())
                    {
                        SetState(State.BRAKE);
                    }
                    else
                    {
                        // Play the stop SFX when boost is released in midair
                        Plugin.StopBoostLoopSfx(p);
                        Plugin.PlayBoostSfx(p, Plugin.Instance.BoostStopClip, false, 1f);
                        p.StopCurrentAbility();
                    }
                }
                else if (inAir)
                {
                    if (!this.isHyperJump)
                    {
                        int curAnim = Plugin.GetMemberValue(p, "curAnim") is int ca ? ca : 0;
                        if (p.motor.velocity.y <= 0f && curAnim == jumpHash)
                        {
                            p.PlayAnim(fallHash);
                        }
                    }

                    if (p.IsGrounded())
                    {
                        SetState(State.BOOST);
                        inAir = false;
                    }
                }
                else if (!p.IsGrounded())
                {
                    LeaveGround();
                }
            }
            else if (state == State.BRAKE)
            {
                int curAnim = Plugin.GetMemberValue(p, "curAnim") is int ca ? ca : 0;
                Vector3 moveInput = Plugin.GetMemberValue(p, "moveInput") is Vector3 mi ? mi : Vector3.zero;

                if (curAnim != boostBrakeHash || (moveInput != Vector3.zero && stateTimer > 0.5f) || !p.IsGrounded())
                {
                    p.StopCurrentAbility();
                }
            }
        }

        public override void OnJump()
        {
            LeaveGround();
        }

        private void LeaveGround()
        {
            if (state == State.START_BOOST)
            {
                targetSpeed = 0f; // Keep horizontal movement frozen if the player jumps during charging
            }
            else
            {
                targetSpeed = p.maxMoveSpeed;
            }

            rotateSpeed = 1f;

            float airDecc = 10f;
            object statsObj = Plugin.GetMemberValue(p, "stats");
            if (statsObj != null)
            {
                object airDeccVal = Plugin.GetMemberValue(statsObj, "airDecc");
                if (airDeccVal is float f) airDecc = f;
            }

            decc = airDecc / 1.5f;
            inAir = true;

            if (!this.isHyperJump && state == State.BOOST)
            {
                string animStr = GetAnimString(false, false);
                PlayMovestyleAnimation(animStr);
            }
        }

        public override void OnStopAbility()
        {
            _isAborted = true;

            // Restore controls if the ability is interrupted
            if (isWaitingForQTE)
            {
                isWaitingForQTE = false;
                QTEManager.Instance.CancelQTE();
            }

            SetHitboxesActive(false);
            RestoreAnimator();
            Plugin.SetMemberValue(p, "boosting", false);

            Plugin.StopBoostLoopSfx(p);

            CharacterVisual charVis = Plugin.GetCharacterVisual(p);
            if (Plugin.Instance.MirrorBoostParticles.Value && charVis != null)
            {
                charVis.SetBoostpackEffect(BoostpackEffectMode.OFF);
            }
        }

        private void RestoreAnimator()
        {
            if (_abilityAnimator != null && _originalController != null && _abilityAnimator.runtimeAnimatorController != _originalController)
            {
                Plugin.SwapAnimatorControllerSafely(_abilityAnimator, _originalController);
                _originalController = null;
            }
        }
    }

    // Bunch Of Emotes support
    public static class BunchOfEmotesSupport
    {
        public static bool Installed { get; private set; } = false;

        private static object BoEPluginInstance = null;
        private static Type BoEPluginType = null;

        private static FieldInfo CustomAnimsField = null;
        private static FieldInfo ControllerField = null;

        private static bool Cached = false;
        private static Dictionary<string, int> GameAnimationByCustomAnimation = new Dictionary<string, int>();
        private static RuntimeAnimatorController CachedController = null;

        public static void Initialize()
        {
            Installed = true;

            BoEPluginInstance = Chainloader.PluginInfos["com.Dragsun.BunchOfEmotes"].Instance;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType("BunchOfEmotes.BunchOfEmotesPlugin");
                if (type != null) { BoEPluginType = type; break; }
            }

            if (BoEPluginType != null)
            {
                CustomAnimsField = BoEPluginType.GetField("myCustomAnims2") ?? BoEPluginType.GetField("myCustomAnims");
                ControllerField = BoEPluginType.GetField("myAnim", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            }
        }

        public static RuntimeAnimatorController GetController()
        {
            if (!Installed || ControllerField == null || BoEPluginInstance == null) return null;

            if (CachedController == null)
            {
                CachedController = ControllerField.GetValue(BoEPluginInstance) as RuntimeAnimatorController;
            }
            return CachedController;
        }

        public static void CacheAnimationsIfNecessary()
        {
            if (Cached) return;

            var boeDictionary = CustomAnimsField?.GetValue(BoEPluginInstance) as Dictionary<int, string>;
            if (boeDictionary != null && boeDictionary.Count > 0)
                Cached = true;
            else
                return;

            foreach (var customAnim in boeDictionary)
            {
                GameAnimationByCustomAnimation[customAnim.Value] = customAnim.Key;
            }
        }

        public static bool TryGetGameAnimationForCustomAnimation(string customAnim, out int gameAnim)
        {
            if (!Installed)
            {
                gameAnim = 0;
                return false;
            }

            CacheAnimationsIfNecessary();

            if (GameAnimationByCustomAnimation.TryGetValue(customAnim, out gameAnim))
                return true;

            gameAnim = 0;
            return false;
        }
    }
}