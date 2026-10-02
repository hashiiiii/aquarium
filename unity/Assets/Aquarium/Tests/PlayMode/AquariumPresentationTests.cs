using System;
using System.Collections;
using Aquarium.Core;
using Aquarium.Presentation;
using Aquarium.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Aquarium.Tests
{
    public sealed class AquariumPresentationTests
    {
        private GameObject root;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (root != null) UnityEngine.Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator TankCreatesThreeSpeciesAndSurvivesRepeatedSync()
        {
            root = new GameObject("Tank test");
            var tank = root.AddComponent<TankView>();
            tank.Initialize();
            var creatures = new[]
            {
                new CreatureVisual("tide_sprite", 0, 0, true),
                new CreatureVisual("moon_jelly", 1, .5f),
                new CreatureVisual("coral_drake", 2, 1)
            };
            tank.SyncCreatures(creatures);
            tank.SyncCreatures(creatures);
            tank.PlayFeedEffect();
            yield return null;
            Assert.That(tank.TankCamera, Is.Not.Null);
            Assert.That(root.transform.Find("Creature moon_jelly"), Is.Not.Null);
            Assert.That(root.transform.Find("Creature coral_drake"), Is.Not.Null);
            var count = 0;
            foreach (Transform child in root.transform) if (child.name.StartsWith("Creature ")) count++;
            Assert.That(count, Is.EqualTo(3));
            tank.SyncCreatures(new[] { creatures[0] });
            yield return null;
            Assert.That(root.transform.Find("Creature moon_jelly"), Is.Null);
        }

        [UnityTest]
        public IEnumerator HudWiresCareAndAdoptionWithoutSavingTestProgress()
        {
            root = new GameObject("HUD test");
            var state = AquariumSimulation.CreateNew(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var hud = root.AddComponent<AquariumHud>();
            var fed = 0;
            var inspected = 0;
            hud.Initialize(() => fed++, () => { }, () => { }, id => AquariumSimulation.Acquire(state, id), id => inspected++);
            hud.Refresh(state, "tide_sprite", "Ready");
            yield return null;
            Assert.That(EventSystem.current.currentInputModule, Is.TypeOf<InputSystemUIInputModule>());
            FindButton("Species moon_jelly").onClick.Invoke();
            hud.Refresh(state, "moon_jelly", "Adopted");
            Assert.That(state.creatures.Count, Is.EqualTo(2));
            FindButton("Species moon_jelly Owned").onClick.Invoke();
            Assert.That(inspected, Is.EqualTo(1));
            state.fullness = 50;
            hud.Refresh(state, "moon_jelly", "Hungry");
            var feed = FindButton("Feed");
            Assert.That(feed.interactable, Is.True);
            feed.onClick.Invoke();
            Assert.That(fed, Is.EqualTo(1));
        }

        private Button FindButton(string name)
        {
            foreach (var button in root.GetComponentsInChildren<Button>())
                if (button.name == name) return button;
            Assert.Fail("Missing button: " + name);
            return null;
        }
    }
}
