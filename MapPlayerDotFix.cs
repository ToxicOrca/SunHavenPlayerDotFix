using System;
using System.Linq;
using System.Collections;
using BepInEx;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[BepInPlugin("com.toxicorca.sunhaven.mapplayerdotfix", "Sun Haven Map Player Dot Fix", "0.6.6")]
public class MapPlayerDotFix : BaseUnityPlugin
{
    private const float Y_OFFSET = 24f;

    // How often we rescan player markers when map is open
    private const float PLAYER_SCAN_INTERVAL = 0.25f;

    // Slow fallback scan (should almost never run now)
    private const float FALLBACK_SCAN_INTERVAL = 10f;

    private Transform _mapImage;
    private Transform _sunHavenNpcImages;

    private float _nextPlayerScanTime = 0f;
    private float _nextFallbackScanTime = 0f;

    private RectTransform _bestRT;
    private Image _bestImg;

    private float _appliedOffset = 0f;

    private Coroutine _eofLoop;

    // This path came directly from your logs
    private const string MAPIMAGE_PATH =
        "UI_Inventory/Inventory/Map/Background/Scroll View/Viewport/Content/MapImage";

    private void Awake()
    {
        Logger.LogInfo("Map Player Dot Fix loaded. (v0.6.6 no-hitch)");
        SceneManager.sceneLoaded += (_, __) => ResetState();
        ResetState();
    }

    private void ResetState()
    {
        _mapImage = null;
        _sunHavenNpcImages = null;
        _bestRT = null;
        _bestImg = null;

        _appliedOffset = 0f;

        _nextPlayerScanTime = 0f;
        _nextFallbackScanTime = 0f;

        if (_eofLoop != null)
        {
            StopCoroutine(_eofLoop);
            _eofLoop = null;
        }
        _eofLoop = StartCoroutine(EndOfFrameLoop());
    }

    private IEnumerator EndOfFrameLoop()
    {
        while (true)
        {
            yield return new WaitForEndOfFrame();

            // FAST PATH: grab MapImage via Player(Clone) hierarchy (cheap)
            _mapImage = FindMapImageFast();

            // If fast path fails, do a very rare fallback scan (expensive)
            if (_mapImage == null && Time.unscaledTime >= _nextFallbackScanTime)
            {
                _nextFallbackScanTime = Time.unscaledTime + FALLBACK_SCAN_INTERVAL;
                _mapImage = FindVisibleMapImageRootFallback();
            }

            if (_mapImage == null || !_mapImage.gameObject.activeInHierarchy)
            {
                // Map not open -> do nothing (no work while walking around)
                _sunHavenNpcImages = null;
                _bestRT = null;
                _bestImg = null;
                _appliedOffset = 0f;
                continue;
            }

            if (_sunHavenNpcImages == null || !_sunHavenNpcImages.gameObject)
            {
                _sunHavenNpcImages = FindChildByName(_mapImage, "SunHavenNPCImages");
                _bestRT = null;
                _bestImg = null;
                _appliedOffset = 0f;
                _nextPlayerScanTime = 0f;
            }

            if (_sunHavenNpcImages == null)
                continue;

            // Rescan candidates occasionally (UI may rebuild while map open)
            if (Time.unscaledTime >= _nextPlayerScanTime || _bestRT == null || _bestImg == null)
            {
                _nextPlayerScanTime = Time.unscaledTime + PLAYER_SCAN_INTERVAL;
                PickBestPlayerMarkerAndDisableClones();
            }

            // Apply offset every frame while map is open (cheap)
            ApplyOffsetToBest();
        }
    }

    private Transform FindMapImageFast()
    {
        try
        {
            var player = GameObject.Find("Player(Clone)");
            if (player == null) return null;

            var t = player.transform.Find(MAPIMAGE_PATH);
            if (t != null && t.gameObject.activeInHierarchy)
                return t;
        }
        catch { /* ignore */ }

        return null;
    }

    private void PickBestPlayerMarkerAndDisableClones()
    {
        try
        {
            var candidates = _sunHavenNpcImages
                .GetComponentsInChildren<Transform>(true)
                .Where(t => t.name.StartsWith("Player", StringComparison.Ordinal) && t.GetComponent<Image>() != null)
                .ToArray();

            if (candidates.Length == 0)
                return;

            var bestT = candidates
                .OrderByDescending(t => t.gameObject.activeInHierarchy)
                .ThenByDescending(t => t.gameObject.activeSelf)
                .ThenByDescending(t => t.GetComponent<Image>().enabled)
                .FirstOrDefault();

            if (bestT == null)
                return;

            // Disable clones to stop flashing/doubles
            foreach (var t in candidates)
            {
                if (t == bestT) continue;
                if (t.gameObject.activeSelf) t.gameObject.SetActive(false);
            }

            var rt = bestT as RectTransform;
            var img = bestT.GetComponent<Image>();
            if (rt == null || img == null)
                return;

            if (!bestT.gameObject.activeSelf) bestT.gameObject.SetActive(true);

            img.enabled = true;
            img.raycastTarget = true;
            var col = img.color; col.a = 1f; img.color = col;

            rt.localScale = Vector3.one;
            rt.SetAsLastSibling();

            _bestRT = rt;
            _bestImg = img;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
        }
    }

    private void ApplyOffsetToBest()
    {
        if (_bestRT == null || _bestImg == null)
            return;

        if (!_bestRT.gameObject || !_bestRT.gameObject.activeInHierarchy)
        {
            _bestRT = null;
            _bestImg = null;
            return;
        }

        _bestImg.enabled = true;
        _bestImg.raycastTarget = true;
        var col = _bestImg.color; col.a = 1f; _bestImg.color = col;

        // Apply offset WITHOUT stacking
        var pos = _bestRT.anchoredPosition;
        var raw = new Vector2(pos.x, pos.y - _appliedOffset);

        _bestRT.anchoredPosition = raw + new Vector2(0f, Y_OFFSET);
        _appliedOffset = Y_OFFSET;

        _bestRT.SetAsLastSibling();
    }

    // Rare fallback
    private Transform FindVisibleMapImageRootFallback()
    {
        try
        {
            foreach (var canvas in Resources.FindObjectsOfTypeAll<Canvas>())
            {
                var all = canvas.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    var t = all[i];
                    if (t != null &&
                        string.Equals(t.name, "MapImage", StringComparison.OrdinalIgnoreCase) &&
                        t.gameObject.activeInHierarchy)
                        return t;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
        }

        return null;
    }

    private static Transform FindChildByName(Transform root, string name)
    {
        if (root == null) return null;
        var all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            var t = all[i];
            if (t != null && t.name == name)
                return t;
        }
        return null;
    }
}
