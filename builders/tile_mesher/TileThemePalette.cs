using System.Collections.Generic;
using UnityEngine;

namespace MeshBuilder
{
    [CreateAssetMenu(fileName = "theme_palette", menuName = "Custom/ThemePalette", order = 1)]
    public class TileThemePalette : ScriptableObject
    {
        [SerializeField]
        private PaletteElem[] elems = null;
        
        public void Init()
        {
            if (elems != null && elems.Length > 0)
            {
                foreach (var elem in elems)
                {
                    if (elem == null)
                    {
                        Debug.LogError("palette has null elem!");
                        continue;
                    }

                    if (elem.Theme == null)
                    {
                        Debug.LogError("palette elem has null theme!");
                        continue;
                    }

                    elem.Theme.Init();
                }
            }
        }

        public int CollectOpenThemeFillValueFlags(TileTheme theme)
        {
            int flags = 0;
            int index = FindThemeIndex(theme);
            if (index >= 0 && elems[index].OpenTowardsThemes != null)
            {
                foreach (var openTheme in elems[index].OpenTowardsThemes)
                {
                    int otherIndex = FindThemeIndex(openTheme);
                    if (otherIndex >= 0)
                    {
                        int otherFillValue = elems[otherIndex].FillValue;
                        if (otherFillValue > 31)
                        {
                            Debug.LogError("fille value can't be used as a flag, you are either using too many themes or this should be reimplemented!");
                        }
                        else
                        {
                            flags |= 1 << otherFillValue;
                        }
                    }
                }
            }
            return flags;
        }
        
        public int FindThemeIndex(string name)
        {
            if (elems != null)
            {
                for (int i = 0; i < elems.Length; ++i)
                {
                    if (elems[i] == null || elems[i].Theme == null)
                    {
                        continue;
                    }

                    if (elems[i].Theme.ThemeName == name)
                    {
                        return i;
                    }
                }
            }
            return -1;
        }

        public int FindThemeIndex(TileTheme theme)
        {
            if (elems != null)
            {
                for (int i = 0; i < elems.Length; ++i)
                {
                    if (elems[i] == null || elems[i].Theme == null)
                    {
                        continue;
                    }

                    if (elems[i].Theme == theme)
                    {
                        return i;
                    }
                }
            }
            return -1;
        }

        public int GetFillValue(int index)
        {
            return elems[index].FillValue;
        }

        public TileTheme Get(int index)
        {
            return elems[index].Theme;
        }

        public int ThemeCount { get { return elems != null ? elems.Length : 0; } }

        [System.Serializable]
        public class PaletteElem
        {
            [SerializeField]
            private TileTheme theme = null;
            public TileTheme Theme { get { return theme; } }

            [SerializeField]
            private int fillValue = 1;
            public int FillValue { get { return fillValue; } }

            [SerializeField]
            private string[] openTowardsThemes = null;
            public string[] OpenTowardsThemes { get { return openTowardsThemes; } }
        }
    }
}
