// Asset registry built by the editor (Assets/Editor/MoeRegistryBuilder.cs) into
// Assets/Resources/MoeRegistry.asset: character models + pose clips, and everything the
// Unity AI session generates under Assets/Generated (animations, audio, textures, materials).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoeGames
{
    public class MoeRegistry : ScriptableObject
    {
        [Serializable]
        public class CharacterEntry
        {
            public string id;
            public GameObject model;
            public AnimationClip pose;
        }

        [Serializable]
        public class NamedClip
        {
            /// <summary>Lower-case asset name, e.g. "whale_run" or "any_attack".</summary>
            public string name;
            /// <summary>Folder under Assets/Generated: "Shared" or "Game1".."Game12".</summary>
            public string scope;
            public AnimationClip clip;
        }

        [Serializable]
        public class NamedAudio { public string name; public string scope; public AudioClip clip; }

        [Serializable]
        public class NamedTexture { public string name; public string scope; public Texture2D texture; }

        [Serializable]
        public class NamedMaterial { public string name; public string scope; public Material material; }

        public List<CharacterEntry> characters = new List<CharacterEntry>();
        public List<NamedClip> animations = new List<NamedClip>();
        public List<NamedAudio> audio = new List<NamedAudio>();
        public List<NamedTexture> textures = new List<NamedTexture>();
        public List<NamedMaterial> materials = new List<NamedMaterial>();

        static MoeRegistry instance;
        static bool loaded;

        public static MoeRegistry I
        {
            get
            {
                if (!loaded)
                {
                    loaded = true;
                    instance = Resources.Load<MoeRegistry>("MoeRegistry");
                }
                return instance;
            }
        }

        /// <summary>Forget the cached instance (the editor calls this after rebuilding).</summary>
        public static void Reload() { loaded = false; instance = null; }

        public CharacterEntry Character(string id)
        {
            id = Cast.Canonical(id);
            foreach (var c in characters) if (c.id == id) return c;
            return null;
        }

        /// <summary>Scope-aware lookup: a game's own folder wins over Shared.</summary>
        static T Find<T>(List<T> list, Func<T, string> name, Func<T, string> scope, string key, string preferScope) where T : class
        {
            key = key.ToLowerInvariant();
            T shared = null;
            foreach (var e in list)
            {
                if (name(e) != key) continue;
                if (preferScope != null && scope(e) == preferScope) return e;
                if (shared == null) shared = e;
            }
            return shared;
        }

        public AnimationClip Anim(string key, string scope = null) => Find(animations, e => e.name, e => e.scope, key, scope)?.clip;
        public AudioClip Audio(string key, string scope = null) => Find(audio, e => e.name, e => e.scope, key, scope)?.clip;
        public Texture2D Texture(string key, string scope = null) => Find(textures, e => e.name, e => e.scope, key, scope)?.texture;
        public Material Material(string key, string scope = null) => Find(materials, e => e.name, e => e.scope, key, scope)?.material;
    }
}
