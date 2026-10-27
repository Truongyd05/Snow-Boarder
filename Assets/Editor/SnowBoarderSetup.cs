using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SnowBoarderLab;

[InitializeOnLoad]
public static class SnowBoarderSetup
{
    static SnowBoarderSetup(){EditorApplication.delayCall += Setup;}
    [MenuItem("Snow Boarder/Open Game Scene")]
    public static void Open(){Setup();EditorSceneManager.OpenScene("Assets/Scenes/SnowBoarder.unity");}
    [MenuItem("Snow Boarder/Build Windows Game")]
    public static void Build()
    {
        Setup();Directory.CreateDirectory("Build/Windows");
        var report=BuildPipeline.BuildPlayer(new[]{"Assets/Scenes/SnowBoarder.unity"},"Build/Windows/SnowBoarder.exe",BuildTarget.StandaloneWindows64,BuildOptions.None);
        Debug.Log("Snow Boarder build: "+report.summary.result);
    }
    public static void PrepareProject(){Setup();}
    static void Setup()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)return;
        if(EditorApplication.isCompiling){EditorApplication.delayCall+=Setup;return;}
        var spriteShader=Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
        if(!File.Exists("Assets/Resources/SnowSprite.mat"))AssetDatabase.CreateAsset(new Material(spriteShader),"Assets/Resources/SnowSprite.mat");
        var spriteAsset=AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/SnowSprite.mat");
        if(spriteAsset.shader!=spriteShader){spriteAsset.shader=spriteShader;EditorUtility.SetDirty(spriteAsset);AssetDatabase.SaveAssets();}
        if(!File.Exists("Assets/Resources/SnowMesh.mat"))AssetDatabase.CreateAsset(new Material(Shader.Find("Universal Render Pipeline/2D/Mesh2D-Unlit-Default")),"Assets/Resources/SnowMesh.mat");
        string art="Assets/Resources/Art/";
        foreach(string name in new[]{"Snowflake","Boost","Shield","Finish"})
        {
            string path=art+name+".png";
            if(!File.Exists(path))
            {
                var tex=new Texture2D(128,128,TextureFormat.RGBA32,false);
                for(int y=0;y<128;y++)for(int x=0;x<128;x++)
                {
                    float px=(x-64)/64f,py=(y-64)/64f;Color c=Color.clear;
                    if(name=="Snowflake")
                    {
                        float r=Mathf.Sqrt(px*px+py*py),a=Mathf.Atan2(py,px);
                        float spoke=Mathf.Abs(Mathf.Sin(3*a));
                        if(r<.86f&&(spoke<.13f||r<.18f||(r>.43f&&r<.57f&&Mathf.Abs(Mathf.Sin(6*a))<.45f)))c=Color.white;
                    }
                    else if(name=="Boost")
                    {
                        if(px*px+py*py<.88f)c=new Color(.08f,.70f,.78f);
                        if(py>-.65f&&py<.7f&&Mathf.Abs(px-(py>.02f?-.08f:.10f))<.14f)c=new Color(1,.86f,.25f);
                    }
                    else if(name=="Shield")
                    {
                        if(py<.7f&&py>-.75f&&Mathf.Abs(px)<(.6f*Mathf.Clamp01((py+.8f)*2)))c=new Color(.28f,.62f,1);
                        if(Mathf.Abs(px)<.08f&&Mathf.Abs(py)<.4f||Mathf.Abs(py)<.07f&&Mathf.Abs(px)<.3f)c=Color.white;
                    }
                    else
                    {
                        if(px>-.73f&&px<-.64f)c=new Color(.13f,.22f,.3f);
                        if(px>-.64f&&px<.75f&&py>.1f&&py<.78f)c=((x/14+y/14)%2==0)?Color.white:new Color(.12f,.25f,.32f);
                    }
                    tex.SetPixel(x,y,c);
                }
                tex.Apply();File.WriteAllBytes(path,tex.EncodeToPNG());Object.DestroyImmediate(tex);AssetDatabase.ImportAsset(path);
            }
        }
        foreach(string file in Directory.GetFiles(art,"*.png"))
        {
            var importer=(TextureImporter)AssetImporter.GetAtPath(file.Replace('\\','/'));
            if(importer==null)continue;
            if(importer.textureType!=TextureImporterType.Sprite||importer.spriteImportMode!=SpriteImportMode.Single||importer.spritePixelsPerUnit!=100)
            {
                importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;importer.spritePixelsPerUnit=100;importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.SaveAndReimport();
            }
        }
        string scenePath="Assets/Scenes/SnowBoarder.unity";
        if(!File.Exists(scenePath))
        {
            var previous=SceneManager.GetActiveScene();
            if(string.IsNullOrEmpty(previous.path))
            {
                if(!Application.isBatchMode)return;
                EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
                previous=SceneManager.GetActiveScene();
            }
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            var go=new GameObject("Snow Boarder - Runtime Course Builder");SceneManager.MoveGameObjectToScene(go,scene);go.AddComponent<SnowBoarderGame>();
            EditorSceneManager.SaveScene(scene,scenePath);EditorSceneManager.CloseScene(scene,true);
            if(previous.IsValid())SceneManager.SetActiveScene(previous);
        }
        EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(scenePath,true)};

    }
}








