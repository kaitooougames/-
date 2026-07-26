using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace TreasureGame
{
public enum TreasureType { Gold, Painting, Jewel, Relic }
public enum Authenticity { Real, Fake }
public enum TreasureLocation { Hand, Display }

public class Treasure : MonoBehaviour
{
    [SerializeField] private TreasureType type;
    [SerializeField] private Authenticity authenticity;
    [SerializeField] private TreasureLocation location;

    private Player owner;
    private TreasureController controller;
    private Quaternion layoutRotation = Quaternion.identity;
    private Vector3 layoutPosition;
    private Vector3 baseScale = Vector3.one;
    private Mesh cardMesh;
    private MeshRenderer cardRenderer;
    private Material frontMaterial;
    private Material backMaterial;
    private Color frontBaseColor = Color.white;
    private Color backBaseColor = Color.white;
    private bool faceUp;
    private bool interactable;
    private bool dimWhenDisabled = true;
    private bool highlighted;

    public TreasureType Type => type;
    public Authenticity Authenticity => authenticity;
    public TreasureLocation Location => location;
    public Player Owner => owner;
    public bool IsFaceUp => faceUp;

    public void Initialize(
        TreasureType treasureType,
        Authenticity cardAuthenticity,
        Player cardOwner,
        TreasureController gameController,
        Material front,
        Material back,
        Vector3 cardSize)
    {
        type = treasureType;
        authenticity = cardAuthenticity;
        owner = cardOwner;
        controller = gameController;
        BuildSinglePlate(front, back, cardSize);
        SetFaceUp(true);
        name = $"{TypeLabel(type)}_{AuthenticityLabel(authenticity)}_P{owner.PlayerId + 1}";
    }

    public void InitializeFromSceneTemplate(
        TreasureType treasureType,
        Authenticity cardAuthenticity,
        Player cardOwner,
        TreasureController gameController)
    {
        type = treasureType;
        authenticity = cardAuthenticity;
        owner = cardOwner;
        controller = gameController;
        baseScale = transform.localScale;

        cardRenderer = GetComponent<MeshRenderer>();
        Material[] sourceMaterials = cardRenderer != null ? cardRenderer.sharedMaterials : null;
        if (sourceMaterials == null || sourceMaterials.Length == 0)
        {
            Debug.LogError($"{name} に表面マテリアルがありません。");
            return;
        }

        frontMaterial = CreateTintableMaterial(sourceMaterials[0]);
        backMaterial = CreateTintableMaterial(sourceMaterials.Length > 1 ? sourceMaterials[1] : sourceMaterials[0]);
        frontBaseColor = ReadBaseColor(frontMaterial);
        backBaseColor = ReadBaseColor(backMaterial);

        cardRenderer.shadowCastingMode = ShadowCastingMode.Off;
        cardRenderer.receiveShadows = false;
        cardRenderer.lightProbeUsage = LightProbeUsage.Off;
        cardRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

        foreach (Collider cardCollider in GetComponents<Collider>())
            cardCollider.enabled = true;

        SetFaceUp(true);
        name = $"{TypeLabel(type)}_{AuthenticityLabel(authenticity)}_P{owner.PlayerId + 1}";
    }

    private void BuildSinglePlate(Material front, Material back, Vector3 size)
    {
        float halfWidth = size.x * 0.5f;
        float halfHeight = size.z * 0.5f;
        float surfaceOffset = Mathf.Max(0.0005f, size.y * 0.5f);

        cardMesh = new Mesh { name = "TreasureCard_TwoSided" };
        cardMesh.vertices = new[]
        {
            new Vector3(-halfWidth, surfaceOffset, -halfHeight),
            new Vector3(-halfWidth, surfaceOffset,  halfHeight),
            new Vector3( halfWidth, surfaceOffset,  halfHeight),
            new Vector3( halfWidth, surfaceOffset, -halfHeight),
            new Vector3(-halfWidth, -surfaceOffset, -halfHeight),
            new Vector3(-halfWidth, -surfaceOffset,  halfHeight),
            new Vector3( halfWidth, -surfaceOffset,  halfHeight),
            new Vector3( halfWidth, -surfaceOffset, -halfHeight)
        };
        cardMesh.uv = new[]
        {
            new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0),
            new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1), new Vector2(0, 0)
        };
        cardMesh.subMeshCount = 2;
        cardMesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
        // 裏面も表面と同じ法線向きで作り、Render Face = Backで反対側だけ描画する。
        cardMesh.SetTriangles(new[] { 4, 5, 6, 4, 6, 7 }, 1);
        cardMesh.RecalculateNormals();
        cardMesh.RecalculateBounds();

        MeshFilter meshFilter = gameObject.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = cardMesh;

        frontMaterial = CreateTintableMaterial(front);
        backMaterial = CreateTintableMaterial(back);
        ConfigureFrontAndBackRendering();
        frontBaseColor = ReadBaseColor(frontMaterial);
        backBaseColor = ReadBaseColor(backMaterial);

        cardRenderer = gameObject.AddComponent<MeshRenderer>();
        cardRenderer.sharedMaterials = new[] { frontMaterial, backMaterial };
        cardRenderer.shadowCastingMode = ShadowCastingMode.Off;
        cardRenderer.receiveShadows = false;
        cardRenderer.lightProbeUsage = LightProbeUsage.Off;
        cardRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    public void SetOwner(Player player) => owner = player;
    public void SetLocation(TreasureLocation newLocation) => location = newLocation;

    public void SetVisibleToLocalPlayer(bool visible)
    {
        if (cardRenderer != null) cardRenderer.enabled = visible;
        foreach (Collider cardCollider in GetComponents<Collider>())
            cardCollider.enabled = visible;
    }

    public void SetFaceUp(bool value)
    {
        faceUp = value;
        ApplyDisplayedFace();
        ApplyRotation();
    }

    public void SetInteractable(bool value)
    {
        SetInteractionState(value, true);
    }

    public void SetInteractionState(bool value, bool shouldDimWhenDisabled)
    {
        interactable = value;
        dimWhenDisabled = shouldDimWhenDisabled;
        if (!interactable && highlighted)
        {
            highlighted = false;
            transform.localScale = baseScale;
            transform.position = layoutPosition;
        }
        ApplyBrightness();
    }

    public void MoveTo(Vector3 position, Quaternion rotation)
    {
        layoutPosition = position;
        layoutRotation = rotation;
        transform.position = highlighted ? layoutPosition + Vector3.up * 0.1f : layoutPosition;
        ApplyRotation();
    }

    private void ApplyRotation()
    {
        transform.rotation = DisplayedRotation(layoutRotation);
    }

    private void ApplyDisplayedFace()
    {
        if (cardRenderer == null) return;
        ConfigureFrontAndBackRendering();
        cardRenderer.sharedMaterials = new[] { frontMaterial, backMaterial };
    }

    private Quaternion DisplayedRotation(Quaternion baseRotation)
    {
        // 手札・展示それぞれのアンカーが持つ角度をそのまま使う。
        // 表へめくる180度回転は勝利演出中だけ行う。
        return baseRotation;
    }

    private bool draggingHand;
    private bool handPointerDown;
    private Vector3 pressPosition;
    private Vector3 lastDragPosition;
    private bool CanDragPlayerOneHand =>
        controller != null && owner != null && owner.PlayerId == 0 && location == TreasureLocation.Hand;

    private void OnMouseDown()
    {
        if (!interactable && !CanDragPlayerOneHand) return;
        handPointerDown = true;
        TreasureTurnPrototype.Instance?.BeginCardHandGrip();
        draggingHand = false;
        pressPosition = Input.mousePosition;
        lastDragPosition = pressPosition;
    }

    private void OnMouseDrag()
    {
        if (!handPointerDown || !CanDragPlayerOneHand) return;
        Vector3 current = Input.mousePosition;
        if (!draggingHand && Vector3.Distance(pressPosition, current) >= 10f)
            draggingHand = true;
        if (draggingHand)
        {
            float deltaX = current.x - lastDragPosition.x;
            if (TreasureTurnPrototype.Instance != null)
                TreasureTurnPrototype.Instance.DragHandFromCard(deltaX);
            else
                controller.DragPlayerOneHand(deltaX);
        }
        lastDragPosition = current;
    }

    private void OnMouseUp()
    {
        if (!handPointerDown) return;
        handPointerDown = false;
        TreasureTurnPrototype.Instance?.EndCardHandGrip();
        if (!draggingHand && interactable) controller?.HandleTreasureClick(this);
        draggingHand = false;
    }

    private void OnMouseEnter()
    {
        if (!interactable) return;
        highlighted = true;
        transform.localScale = baseScale * 1.14f;
        transform.position = layoutPosition + Vector3.up * 0.1f;
        ApplyBrightness();
    }

    private void OnMouseExit()
    {
        highlighted = false;
        transform.localScale = baseScale;
        transform.position = layoutPosition;
        ApplyBrightness();
    }

    private void ApplyBrightness()
    {
        float brightness = interactable ? (highlighted ? 1.45f : 1.15f)
            : (dimWhenDisabled ? 0.35f : 1f);
        WriteBaseColor(frontMaterial, frontBaseColor, brightness);
        WriteBaseColor(backMaterial, backBaseColor, brightness);

        Color emission = interactable
            ? new Color(0.22f, 0.18f, 0.025f) * (highlighted ? 3f : 1f)
            : Color.black;
        WriteEmission(frontMaterial, emission);
        WriteEmission(backMaterial, emission);
    }

    public void AnimateTo(Vector3 targetPosition, Quaternion targetRotation, float duration)
    {
        StopAllCoroutines();
        StartCoroutine(AnimateRoutine(targetPosition, targetRotation, duration));
    }

    public void AnimateFlipToFaceUp(float duration)
    {
        StopAllCoroutines();
        StartCoroutine(FlipToFaceUpRoutine(duration));
    }

    public void AnimateFlipToFaceDown(float duration)
    {
        StopAllCoroutines();
        StartCoroutine(FlipToFaceDownRoutine(duration));
    }

    private IEnumerator FlipToFaceUpRoutine(float duration)
    {
        // 回転前に表＝Front、裏＝Backを固定し、以降は素材を一切変更しない。
        ApplyDisplayedFace();
        Quaternion startRotation = layoutRotation;
        transform.rotation = startRotation;
        float flipDuration = Mathf.Max(0.01f, duration);
        float elapsed = 0f;

        while (elapsed < flipDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / flipDuration);
            t = t * t * (3f - 2f * t);
            float zAngle = Mathf.Lerp(0f, -180f, t);
            transform.rotation = startRotation * Quaternion.AngleAxis(zAngle, Vector3.forward);
            yield return null;
        }

        transform.rotation = startRotation * Quaternion.AngleAxis(-180f, Vector3.forward);
        faceUp = true;
    }

    private IEnumerator FlipToFaceDownRoutine(float duration)
    {
        ApplyDisplayedFace();
        Quaternion startRotation = layoutRotation * Quaternion.AngleAxis(-180f, Vector3.forward);
        Quaternion targetRotation = layoutRotation;
        transform.rotation = startRotation;
        float flipDuration = Mathf.Max(0.01f, duration);
        float elapsed = 0f;

        while (elapsed < flipDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / flipDuration);
            t = t * t * (3f - 2f * t);
            transform.rotation = Quaternion.Slerp(startRotation, targetRotation, t);
            yield return null;
        }

        transform.rotation = targetRotation;
        faceUp = false;
    }

    private IEnumerator AnimateRoutine(Vector3 targetPosition, Quaternion targetRotation, float duration)
    {
        Vector3 startPosition = transform.position;
        Quaternion startRotation = transform.rotation;
        layoutPosition = targetPosition;
        layoutRotation = targetRotation;
        Quaternion displayedTargetRotation = DisplayedRotation(targetRotation);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            // SmootherStep：開始時と終了時の速度・加速度を0にして、手札の出入りを自然にする。
            t = t * t * t * (t * (t * 6f - 15f) + 10f);
            transform.position = Vector3.Lerp(startPosition, targetPosition, t);
            transform.rotation = Quaternion.Slerp(startRotation, displayedTargetRotation, t);
            yield return null;
        }
        transform.SetPositionAndRotation(targetPosition, displayedTargetRotation);
    }

    private static Color ReadBaseColor(Material material)
    {
        if (material.HasProperty("_BaseColor")) return material.GetColor("_BaseColor");
        if (material.HasProperty("_Color")) return material.GetColor("_Color");
        return Color.white;
    }

    // Unity/Textureなど色を乗算できない元素材も、確実に明暗変更できるURP Unlitへ揃える。
    private static Material CreateTintableMaterial(Material source)
    {
        Texture texture = null;
        if (source.HasProperty("_BaseMap")) texture = source.GetTexture("_BaseMap");
        if (texture == null && source.HasProperty("_MainTex")) texture = source.GetTexture("_MainTex");
        if (texture == null) texture = source.mainTexture;

        Material material = new Material(source);
        Shader tintableShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (tintableShader == null) return material;

        material.shader = tintableShader;
        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
        return material;
    }

    private void ConfigureFrontAndBackRendering()
    {
        // URP Render Face: Front = Cull Back(2)、Back = Cull Front(1)
        if (frontMaterial != null && frontMaterial.HasProperty("_Cull"))
            frontMaterial.SetFloat("_Cull", 2f);
        if (backMaterial != null && backMaterial.HasProperty("_Cull"))
            backMaterial.SetFloat("_Cull", 1f);
    }

    private static void WriteBaseColor(Material material, Color source, float brightness)
    {
        Color result = new Color(source.r * brightness, source.g * brightness, source.b * brightness, source.a);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", result);
        if (material.HasProperty("_Color")) material.SetColor("_Color", result);
    }

    private static void WriteEmission(Material material, Color color)
    {
        if (!material.HasProperty("_EmissionColor")) return;
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", color);
    }

    private void OnDestroy()
    {
        if (cardMesh != null) Destroy(cardMesh);
        if (frontMaterial != null) Destroy(frontMaterial);
        if (backMaterial != null) Destroy(backMaterial);
    }

    public static string TypeLabel(TreasureType value)
    {
        switch (value)
        {
            case TreasureType.Gold: return "金";
            case TreasureType.Painting: return "絵画";
            case TreasureType.Jewel: return "宝石";
            default: return "遺物";
        }
    }

    private static string AuthenticityLabel(Authenticity value)
    {
        return value == Authenticity.Real ? "本物" : "偽物";
    }
}
}
