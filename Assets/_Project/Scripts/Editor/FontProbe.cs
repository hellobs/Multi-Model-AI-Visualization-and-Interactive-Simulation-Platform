using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Mavis.Overlays
{
    /// <summary>运行时字体链路探针：菜单触发，结果打控制台。</summary>
    public static class FontProbe
    {
        [MenuItem("Mavis/Dev/字体链路探针")]
        public static void Probe()
        {
            var sb = new StringBuilder();
            var noto = Resources.Load<Font>("Fonts/NotoSansSC-Regular");
            sb.Append("Noto资产=").Append(noto == null ? "null" : noto.GetType().Name + ":" + noto.name);
            if (noto != null)
            {
                try { sb.Append(", 字体名=").Append(noto.fontNames[0]); }
                catch (System.Exception e) { sb.Append(", 字体名访问异常=").Append(e.Message); }
            }
            sb.Append(" | ");

            var prefab = Resources.Load<GameObject>("Agent");
            sb.Append("Agent预制=").Append(prefab == null ? "null" : "ok");
            TextMesh tmpl = null;
            if (prefab != null)
            {
                try { tmpl = prefab.GetComponentInChildren<TextMesh>(true); }
                catch (System.Exception e) { sb.Append(", 遍历异常=").Append(e.Message); }
            }
            if (tmpl != null)
            {
                sb.Append(", 名牌TextMesh=ok");
                try { sb.Append(", 名牌字体=").Append(tmpl.font == null ? "null" : tmpl.font.name); }
                catch (System.Exception e) { sb.Append(", 名牌字体访问异常=").Append(e.Message); }
            }
            else sb.Append(", 名牌TextMesh=null");

            var clock = GameObject.Find("SimClock");
            sb.Append(" | SimClock=").Append(clock == null ? "无" : "有");
            if (clock != null)
            {
                var tm = clock.GetComponent<TextMesh>();
                sb.Append(", 时钟文本=").Append(tm == null ? "无组件" : "\"" + tm.text + "\"");
                sb.Append(", 时钟字体=").Append(tm == null || tm.font == null ? "null" : tm.font.name);
            }
            Debug.LogWarning("[字体探针] " + sb.ToString());
        }
    }
}
