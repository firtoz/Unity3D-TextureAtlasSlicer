using System;
using System.IO;
using System.Linq;
using System.Xml;
using UnityEditor;
using UnityEngine;

public class TextureAtlasSlicer : EditorWindow {
    [MenuItem("CONTEXT/TextureImporter/Slice Sprite Using XML")]
    public static void SliceUsingXML(MenuCommand command) {
        var textureImporter = command.context as TextureImporter;

        var window = CreateInstance<TextureAtlasSlicer>();

        window.importer = textureImporter;

        window.ShowUtility();
    }

    [MenuItem("Assets/Slice Sprite Using XML")]
    public static void TextureAtlasSlicerWindow() {
        var window = CreateInstance<TextureAtlasSlicer>();

        window.Show();
    }

    [MenuItem("CONTEXT/TextureImporter/Slice Sprite Using XML", true)]
    public static bool ValidateSliceUsingXML(MenuCommand command) {
        var textureImporter = command.context as TextureImporter;

        //valid only if the texture type is 'sprite' or 'advanced'.
        return textureImporter && textureImporter.textureType == TextureImporterType.Sprite ||
               textureImporter.textureType == TextureImporterType.Advanced;
    }

    public TextureImporter importer;

    public TextureAtlasSlicer() {
        titleContent = new GUIContent("XML Slicer");
    }


    [SerializeField]
    private TextAsset xmlAsset;

    public SpriteAlignment spriteAlignment = SpriteAlignment.Center;

    public Vector2 customOffset = new Vector2(0.5f, 0.5f);

    public void OnSelectionChange() {
        UseSelectedTexture();
    }

    private Texture2D selectedTexture;

    private void UseSelectedTexture() {
        if (Selection.objects.Length > 1) {
            selectedTexture = null;
        } else {
            selectedTexture = Selection.activeObject as Texture2D;
        }

        if (selectedTexture != null) {
            var assetPath = AssetDatabase.GetAssetPath(selectedTexture);

            importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;

            if (importer) {
                var extension = Path.GetExtension(assetPath);
                var pathWithoutExtension = assetPath.Remove(assetPath.Length - extension.Length, extension.Length);

                var xmlPath = pathWithoutExtension + ".xml";

                var textAsset = AssetDatabase.LoadAssetAtPath(xmlPath, typeof (TextAsset));

                if (textAsset != null) {
                    xmlAsset = textAsset as TextAsset;
                } else {
                    xmlAsset = null;
                    subTextures = null;
                }

                ParseXML();
            } else {
                xmlAsset = null;
                subTextures = null;
            }
        } else {
            importer = null;
            xmlAsset = null;
            subTextures = null;
        }

        Repaint();
    }

    private SubTexture[] subTextures;
    private int wantedWidth, wantedHeight;

    private void ParseXML() {
        try {
            var document = new XmlDocument();
            document.LoadXml(xmlAsset.text);

            var root = document.DocumentElement;
            if (root == null || root.Name != "TextureAtlas") {
                return;
            }

            subTextures = root.ChildNodes
                              .Cast<XmlNode>()
                              .Where(childNode => childNode.Name == "SubTexture")
                              .Select(childNode => new SubTexture {
                                  width = Convert.ToInt32(childNode.Attributes["width"].Value),
                                  height = Convert.ToInt32(childNode.Attributes["height"].Value),
                                  x = Convert.ToInt32(childNode.Attributes["x"].Value),
                                  y = Convert.ToInt32(childNode.Attributes["y"].Value),
                                  name = childNode.Attributes["name"].Value
                              }).ToArray();

            wantedWidth = 0;
            wantedHeight = 0;

            foreach (var subTexture in subTextures) {
                var right = subTexture.x + subTexture.width;
                var bottom = subTexture.y + subTexture.height;

                wantedWidth = Mathf.Max(wantedWidth, right);
                wantedHeight = Mathf.Max(wantedHeight, bottom);
            }
        } catch (Exception) {
            subTextures = null;
        }
    }

    public void OnEnable() {
        UseSelectedTexture();
    }

    public void OnGUI() {
        if (importer == null) {
            EditorGUILayout.LabelField("Please select a texture to slice.");
            return;
        }
        EditorGUI.BeginDisabledGroup(focusedWindow != this);
        {
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.ObjectField("Texture", Selection.activeObject, typeof (Texture), false);
            EditorGUI.EndDisabledGroup();

            if (importer.textureType != TextureImporterType.Sprite &&
                importer.textureType != TextureImporterType.Advanced) {
                EditorGUILayout.LabelField("The Texture Type needs to be Sprite or Advanced!");
            }

            EditorGUI.BeginDisabledGroup((importer.textureType != TextureImporterType.Sprite &&
                                          importer.textureType != TextureImporterType.Advanced));
            {
                EditorGUI.BeginChangeCheck();
                xmlAsset = EditorGUILayout.ObjectField("XML Source", xmlAsset, typeof (TextAsset), false) as TextAsset;
                if (EditorGUI.EndChangeCheck()) {
                    ParseXML();
                }

                spriteAlignment = (SpriteAlignment) EditorGUILayout.EnumPopup("Pivot", spriteAlignment);

                EditorGUI.BeginDisabledGroup(spriteAlignment != SpriteAlignment.Custom);
                EditorGUILayout.Vector2Field("Custom Offset", customOffset);
                EditorGUI.EndDisabledGroup();

                var needsToResizeTexture = wantedWidth > selectedTexture.width || wantedHeight > selectedTexture.height;

                if (xmlAsset != null && needsToResizeTexture) {
                    EditorGUILayout.LabelField(
                        string.Format("Texture size too small."
                                      + " It needs to be at least {0} by {1} pixels!",
                            wantedWidth,
                            wantedHeight));
                    EditorGUILayout.LabelField("Try changing the Max Size property in the importer.");
                }

                if (subTextures == null || subTextures.Length == 0) {
                    EditorGUILayout.LabelField("Could not find any SubTextures in XML.");
                }

                EditorGUI.BeginDisabledGroup(xmlAsset == null || needsToResizeTexture || subTextures == null ||
                                             subTextures.Length == 0);
                if (GUILayout.Button("Slice")) {
                    PerformSlice();
                }
                EditorGUI.EndDisabledGroup();
            }
            EditorGUI.EndDisabledGroup();
        }
        EditorGUI.EndDisabledGroup();
    }

    private struct SubTexture {
        public int width;
        public int height;
        public int x;
        public int y;
        public string name;
    }

    private void PerformSlice() {
        if (importer == null) {
            return;
        }

        var textureHeight = selectedTexture.height;

        var needsUpdate = false;

        if (importer.spriteImportMode != SpriteImportMode.Multiple) {
            needsUpdate = true;
            importer.spriteImportMode = SpriteImportMode.Multiple;
        }

        var wantedSpriteSheet = (from subTexture in subTextures
                                let actualY = textureHeight - (subTexture.y + subTexture.height)
                                select new SpriteMetaData {
                                    alignment = (int) spriteAlignment,
                                    border = new Vector4(),
                                    name = subTexture.name,
                                    pivot = GetPivotValue(spriteAlignment, customOffset),
                                    rect = new Rect(subTexture.x, actualY, subTexture.width, subTexture.height)
                                }).ToArray();
        if (!needsUpdate && !importer.spritesheet.SequenceEqual(wantedSpriteSheet)) {
            needsUpdate = true;
            importer.spritesheet = wantedSpriteSheet;
        }

        if (needsUpdate) {
            EditorUtility.SetDirty(importer);

            try {
                AssetDatabase.StartAssetEditing();
                AssetDatabase.ImportAsset(importer.assetPath);

                EditorUtility.DisplayDialog("Success!", "The sprite was sliced successfully.", "OK");
            } catch (Exception exception) {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Error", "There was an exception while trying to reimport the image." +
                                                     "\nPlease check the console log for details.", "OK");
            } finally {
                AssetDatabase.StopAssetEditing();
            }
        } else {
            EditorUtility.DisplayDialog("Nope!", "The sprite is already sliced according to this XML file.", "OK");
        }
    }

    //SpriteEditorUtility
    public static Vector2 GetPivotValue(SpriteAlignment alignment, Vector2 customOffset) {
        switch (alignment) {
            case SpriteAlignment.Center:
                return new Vector2(0.5f, 0.5f);
            case SpriteAlignment.TopLeft:
                return new Vector2(0.0f, 1f);
            case SpriteAlignment.TopCenter:
                return new Vector2(0.5f, 1f);
            case SpriteAlignment.TopRight:
                return new Vector2(1f, 1f);
            case SpriteAlignment.LeftCenter:
                return new Vector2(0.0f, 0.5f);
            case SpriteAlignment.RightCenter:
                return new Vector2(1f, 0.5f);
            case SpriteAlignment.BottomLeft:
                return new Vector2(0.0f, 0.0f);
            case SpriteAlignment.BottomCenter:
                return new Vector2(0.5f, 0.0f);
            case SpriteAlignment.BottomRight:
                return new Vector2(1f, 0.0f);
            case SpriteAlignment.Custom:
                return customOffset;
            default:
                return Vector2.zero;
        }
    }
}