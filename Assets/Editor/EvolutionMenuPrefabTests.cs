using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using WorldSim;

// Edit-mode checks for the Evolution menu prefab and its upgrade delay.
// Run them from Window > General > Test Runner > EditMode > Run All. They don't need Play mode or a scene.
public class EvolutionMenuPrefabTests
{
    const string PrefabPath = "Assets/Assets/Images/UI ASSETS/Menu's/EvolutionMenuRoot.prefab";

    GameObject canvas;
    EvolutionMenu menu;

    [SetUp]
    public void SetUp()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<EvolutionMenu>(PrefabPath);
        Assert.IsNotNull(prefab, $"No prefab with an EvolutionMenu component on its root at {PrefabPath}");
        canvas = new GameObject("TestCanvas", typeof(RectTransform), typeof(Canvas));
        menu = Object.Instantiate(prefab, canvas.transform, false);
    }

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(canvas);

    [Test]
    public void PrefabHasEveryPartTheMenuNeeds() =>
        Assert.IsTrue(menu.Bind(), "Bind logged the missing paths above");

    [Test]
    public void EveryRoundedImageHasASprite()
    {
        foreach (Image image in menu.GetComponentsInChildren<Image>(true))
            if (image.type == Image.Type.Sliced)
                Assert.IsNotNull(image.sprite, $"{image.name} is sliced but has no sprite, so it draws as a plain rectangle");
    }

    [Test]
    public void OpenButtonTogglesTheMenu()
    {
        Button openButton = InitializeMenu(out _, out _);
        Assert.IsFalse(menu.IsOpen, "starts hidden");

        openButton.onClick.Invoke();
        Assert.IsTrue(menu.IsOpen);

        Part<Button>("Panel/CloseButton").onClick.Invoke();
        Assert.IsFalse(menu.IsOpen);
    }

    [Test]
    public void ChangingATraitLocksOnlyThatTraitUntilTheDelayPasses()
    {
        InitializeMenu(out PlantModel model, out TickEngine engine);
        int delay = new SerializedObject(menu).FindProperty("upgradeCooldownTicks").intValue;
        Assume.That(delay, Is.GreaterThan(0), "the upgrade delay is 0 on the prefab, so there is nothing to test");

        menu.Open();
        var canopyUp = Part<Button>("Panel/CanopyCard/Up");
        var canopyDown = Part<Button>("Panel/CanopyCard/Down");
        var spreadUp = Part<Button>("Panel/SpreadCard/Up");
        int start = TierTable.DefaultTier;

        canopyUp.onClick.Invoke();
        Assert.AreEqual(start + 1, model.Evolution.GetTier(PlantAttribute.Canopy), "the first upgrade goes through");
        Assert.IsFalse(canopyUp.interactable, "+ is locked on the trait just changed");
        Assert.IsFalse(canopyDown.interactable, "- is locked on the trait just changed");
        Assert.IsTrue(spreadUp.interactable, "other traits stay free");

        canopyUp.onClick.Invoke();
        Assert.AreEqual(start + 1, model.Evolution.GetTier(PlantAttribute.Canopy), "a click while locked is ignored");

        SetTick(engine, delay - 1);
        menu.Open(); // redraws the buttons
        Assert.IsFalse(canopyUp.interactable, "still locked one tick before the delay ends");

        SetTick(engine, delay);
        menu.Open();
        Assert.IsTrue(canopyUp.interactable, "free again once the delay has passed");
        canopyUp.onClick.Invoke();
        Assert.AreEqual(start + 2, model.Evolution.GetTier(PlantAttribute.Canopy));
    }

    // Initializes the menu with a fresh model holding the maximum DNA segments, a stopped tick engine at tick 0
    // and an "Evolution" open button.
    Button InitializeMenu(out PlantModel model, out TickEngine engine)
    {
        model = new PlantModel(new EnvParams(), new GrowthParams());
        model.Evolution.AddIncome(100000f);
        engine = canvas.AddComponent<TickEngine>();

        var open = new GameObject("Evolution", typeof(RectTransform), typeof(Image), typeof(Button));
        open.transform.SetParent(canvas.transform, false);
        var openButton = open.GetComponent<Button>();

        menu.Initialize(model, engine, openButton);
        return openButton;
    }

    T Part<T>(string path) where T : Component => menu.transform.Find(path).GetComponent<T>();

    // TickNumber has a private setter; the engine only advances it in Play mode.
    static void SetTick(TickEngine engine, int tick) =>
        typeof(TickEngine).GetProperty(nameof(TickEngine.TickNumber)).SetValue(engine, tick);
}
