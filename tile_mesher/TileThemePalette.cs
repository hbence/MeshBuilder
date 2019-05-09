using System.Collections.Generic;
using UnityEngine;

namespace MeshBuilder
{
    [CreateAssetMenu(fileName = "theme_palette", menuName = "Custom/ThemePalette", order = 1)]
    public class TileThemePalette : ScriptableObject
    {
        [SerializeField]
        private TileTheme[] themes = null;
        
        public void Init()
        {
            if (themes != null)
            {
                foreach (var theme in themes)
                {
                    theme.Init();
                }
            }
        }
     
        private List<int> CollectOpenThemeIndices(TileTheme theme)
        {
            List<int> results = new List<int>();

            if (theme.OpenTowardsThemes != null && theme.OpenTowardsThemes.Length > 0)
            {
                foreach (var openTheme in theme.OpenTowardsThemes)
                {
                    int index = FindThemeIndex(openTheme);
                    if (index >= 0)
                    {
                        results.Add(index);
                    }
                }
            }

            return results;
        }
        
        public int FindThemeIndex(string name)
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
    }
}
