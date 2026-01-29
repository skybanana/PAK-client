using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class CanvasIntro : MonoBehaviour
{
    [SerializeField] private Image logo;
    [SerializeField] private Sprite logoSprite;
    [SerializeField] private bool unloadIntroAfterLoad = true;

    void Awake()
    {

    }

    private IEnumerator Start()
    {
        if (!SceneManager.GetSceneByName("DontDestroy").isLoaded)
        {
            yield return SceneManager.LoadSceneAsync("DontDestroy", LoadSceneMode.Additive);
        }
        //DataManager.instance.Init();
        //yield return new WaitUntil(() => DataManager.instance.isInit);
        //ResourceManager.instance.Init();
        //yield return new WaitUntil(() => ResourceManager.instance.isInit);
        yield return new WaitForSeconds(2);
        if (!SceneManager.GetSceneByName("Game").isLoaded)
        {
            yield return SceneManager.LoadSceneAsync("Game", LoadSceneMode.Additive);
        }

        var gameScene = SceneManager.GetSceneByName("Game");
        if (gameScene.IsValid())
        {
            SceneManager.SetActiveScene(gameScene);
        }

        if (unloadIntroAfterLoad)
        {
            SceneManager.UnloadSceneAsync("intro");
        }
    }
}
