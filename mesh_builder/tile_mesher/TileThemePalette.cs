using UnityEngine;

namespace MeshBuilder
{
    [CreateAssetMenu(fileName = "theme_palette", menuName = "Custom/ThemePalette", order = 1)]
    public class TileThemePalette : ScriptableObject
    {
        [SerializeField]
        private TileTheme[] themes = null;

        private int[] openFlags;

        public void Init()
        {
            FillOpenFlagsArray();
            foreach (var theme in themes)
            {
                theme.Init();
            }
        }
        
        private void FillOpenFlagsArray()
        {
            if (themes == null)
            {
                Debug.LogError("no themes");
                return;
            }

            if (themes.Length > 32)
            {
                Debug.LogError("you have to either use less than 33 themes in this palette or reimplement the openFlags array IsFilled() check!");
                return;
            }

            if (openFlags != null && openFlags.Length == themes.Length)
            {
                return;
            }

            openFlags = new int[themes.Length];
            for (int i = 0; i < themes.Length; ++i)
            {
                int flags = 0;
                var theme = themes[i];
                var openThemes = theme.OpenTowardsThemes;
                if (openThemes != null && openThemes.Length > 0)
                {
                    for (int j = 0; j < openThemes.Length; ++j)
                    {
                        int themeIndex = FindThemeIndex(openThemes[j]);
                        if (themeIndex >= 0)
                        {
                            flags |= (1 << themeIndex);
                        }
                        else
                        {
                            Debug.LogWarning("can't find theme:" + openThemes[j]);
                        }
                    }
                }
                openFlags[i] = flags;
            }
        }

        private int FindThemeIndex(string name)
        {
            for (int i = 0; i < themes.Length; ++i)
            {
                if (themes[i].ThemeName == name)
                {
                    return i;
                }
            }
            return -1;
        }

        public TileTheme Get(int index)
        {
            return themes[index];
        }

        public int ThemeCount { get { return themes != null ? themes.Length : 0; } }

        public bool IsFilled(byte themeIndex, byte otherIndex)
        {
            return themeIndex == otherIndex || (openFlags != null && openFlags[themeIndex] != 0 && DoesCollide(openFlags[themeIndex], otherIndex));
        }

        static private bool DoesCollide(int value, byte flagNumber)
        {
            return (value & (1 << flagNumber)) != 0;
        }
    }
}
