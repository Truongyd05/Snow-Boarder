using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using SnowBoarderLab;

public class SnowBoarderPlayTests
{
    static void CaptureCourse(SnowBoarderGame game)
    {
        var target=new RenderTexture(1280,720,24);
        target.Create();
        var request=new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination=target };
        Assert.IsTrue(UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(game.View,request));
        game.View.aspect=1280f/720;
        game.View.SubmitRenderRequest(request);
        var previous=RenderTexture.active;RenderTexture.active=target;
        var texture=new Texture2D(1280,720,TextureFormat.RGB24,false);
        texture.ReadPixels(new Rect(0,0,1280,720),0,0);texture.Apply();
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(Application.dataPath,"../Gameplay.png"),texture.EncodeToPNG());
        RenderTexture.active=previous;Object.Destroy(texture);target.Release();Object.Destroy(target);
    }
    [UnityTest]
    public IEnumerator FullRunLifecycle()
    {
        var game=SnowBoarderGame.Instance;
        if(game==null)game=new GameObject("Game under test").AddComponent<SnowBoarderGame>();
        Assert.AreEqual(RunState.Menu,game.State);
        Assert.NotNull(Resources.Load<Sprite>("Art/Snowflake"));
        Assert.NotNull(Resources.Load<Sprite>("Art/Boarder_Top"));
        Assert.NotNull(Resources.Load<AudioClip>("Audio/Crash SFX"));
        if(SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            yield return new WaitForSeconds(1);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.dataPath,"../MainMenu.png"));
            yield return new WaitForSeconds(.3f);
        }
        game.StartRun();float initial=game.Player.Body.position.x;
        yield return new WaitForSeconds(2);
        Assert.Greater(game.Player.Body.position.x,initial+2,"rider must move along slope");
        Assert.Greater(game.Score,0,"motion earns points");
        if(SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            CaptureCourse(game);
            yield return new WaitForSeconds(.3f);
        }
        game.Collect(PickupKind.Snowflake);game.Collect(PickupKind.Snowflake);
        Assert.GreaterOrEqual(game.Combo,2);
        game.Collect(PickupKind.Shield);int lives=game.Lives;game.Crash();Assert.AreEqual(lives,game.Lives);
        game.ShieldUntil=0;game.Player.SafeUntil=0;game.Crash();Assert.AreEqual(lives-1,game.Lives);Assert.AreEqual(0,game.Combo);
        game.Collect(PickupKind.Boost);Assert.Greater(game.BoostUntil,Time.time);
        game.Player.SafeUntil=0;game.Crash();game.Player.SafeUntil=0;game.Crash();Assert.AreEqual(RunState.Lost,game.State);
        game.StartRun();Assert.AreEqual(3,game.Lives);Assert.AreEqual(1,game.Level);Assert.AreEqual(0,game.Score);Assert.AreEqual(0,game.Combo);
        for(int stage=1;stage<=SnowBoarderGame.LevelCount;stage++)
        {
            Assert.AreEqual(stage,game.Level);
            game.Player.Body.position=new Vector2(game.FinishX,SnowBoarderGame.Surface(game.FinishX)+2);
            yield return new WaitForSeconds(.15f);
            Assert.AreEqual(stage<SnowBoarderGame.LevelCount?RunState.LevelComplete:RunState.Won,game.State,"physical finish trigger must advance the stage");
            if(stage<SnowBoarderGame.LevelCount)game.NextLevel();
        }
        Assert.AreEqual(RunState.Won,game.State,"the run ends after the final stage");
        game.StartRun();game.AutoTest=true;
        yield return new WaitForSeconds(3);
        Assert.Greater(game.Player.Body.position.x,5);Assert.IsTrue(float.IsFinite(game.Player.Body.position.y));
        Assert.GreaterOrEqual(Object.FindObjectsByType<CoursePickup>(FindObjectsInactive.Include).Length,50);
        Object.Destroy(game.gameObject);
    }
}


