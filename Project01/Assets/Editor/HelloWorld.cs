using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;



// ��ΪAssets�µ������ű��ᱻ���뵽AssetmblyCharp.dll�У������Ű�������ȥ(��APK����
// ���Բ�����ʹ������UnityEditor�����ռ��µķ���
public class HelloWorld : MonoBehaviour
{

    public AssetBundlePattern LoadPattern;

    public Button LoadBundleButton;
    public Button LoadAssetButton;
    public Button UnLoadFalseButton;
    public Button UnLoadTrueButton;

    AssetBundle CubeBundle;
    AssetBundle SphereBundle;
    GameObject CubeObject; // �����¡�����������Ա�����
    GameObject SphereObject;

    // ����İ�����Ӧ����Editor���������Ϊ��Դ������Ҳ��Ҫ���ʣ����з�����Դ��������
    //public static string MainAssetBundleName = "SampleAssetBundle"; // ����������ļ�����
    //public static string ObjectAssetBundleName = "resourcesbundle";

    public string AssetBundleLoadPath;

    // ���ļ��ŵ�HPS�У���ȡ��·��
    public string HTTPAddress = "http://192.168.8.83:8080/";

    public string HTTPAssetBundlePath;

    public string DownloadPath;

    void Start()
    {

        AssetManagerRuntime.AssetManagerInit(LoadPattern);
        AssetPackage assetPackage = AssetManagerRuntime.Instance.LoadPackage("A");
        Debug.Log(assetPackage.PackageName);


        GameObject sampleObject = assetPackage.LoadAsset<GameObject>("Assets/SampleAssets/Cube.prefab");
        Instantiate(sampleObject);


    //    CheckAssetBundleLoadPath();

    //    // �󶨵���¼�
    //    LoadBundleButton.onClick.AddListener(CheckAssetBundlePattern);
    //    LoadAssetButton.onClick.AddListener(LoadAsset);
    //    UnLoadFalseButton.onClick.AddListener(() => { UnLoadAsset(false); });
    //    UnLoadTrueButton.onClick.AddListener(() => { UnLoadAsset(true); });
    }

    void CheckAssetBundleLoadPath() // ����������·��
    {
        switch (LoadPattern) 
        {
            case AssetBundlePattern.EditorSimulation:
                break;
            case AssetBundlePattern.Local:
                AssetBundleLoadPath = Path.Combine(Application.streamingAssetsPath);
                break;
            case AssetBundlePattern.Remote:
                // AB��ŵ��ļ���·��
                HTTPAssetBundlePath = Path.Combine(HTTPAddress);

                // ���ص�AB���ļ���·����AssetBundleLoadPath
                DownloadPath = Path.Combine(Application.persistentDataPath, "DownloadAssetBundle");
                AssetBundleLoadPath = Path.Combine(DownloadPath);

                if (!Directory.Exists(AssetBundleLoadPath))
                {
                    Directory.CreateDirectory(AssetBundleLoadPath);
                }
                break;
        }

    }

    // ��Զ������AB��
    IEnumerator DownloadFile(string fileName, Action callBack, bool isSaveFile = true)
    {
        // Զ��AB�����ļ���·��
        string assetBundleDownloadPath = Path.Combine(HTTPAddress, fileName);

        // HPS�е���ΪSampleAssetBundle����·��http://192.168.8.83:8080/SampleAssetBundle/SampleAssetBundle
        Debug.Log($"AB���Ӹ�·�����أ�{assetBundleDownloadPath}");
        UnityWebRequest webRequest = UnityWebRequest.Get(assetBundleDownloadPath);
        
        yield return webRequest.SendWebRequest();

        while (!webRequest.isDone)
        {
            Debug.Log(webRequest.downloadedBytes); // �������ֽ���
            Debug.Log(webRequest.downloadProgress); // ���ؽ���
            yield return new WaitForEndOfFrame();
        }

        // AB����·��
        string fileSavePath = Path.Combine(AssetBundleLoadPath, fileName);

        //C:/Users/wyh/AppData/LocalLow/DefaultCompany/Test\DownloadAssetBundle
        //                              \SampleAssetBundle\SampleAssetBundle
        Debug.Log($"AB������ĵ�ַΪ��{fileSavePath}");
        Debug.Log(webRequest.downloadHandler.data.Length);
        if (isSaveFile)
        {
            yield return SaveFile(fileSavePath, webRequest.downloadHandler.data, callBack);
        }
        else
        {
            // ��Ŀ������ж϶����Ƿ�Ϊ��
            callBack?.Invoke();
        }
    }

    // ��Զ�̵��ļ�д�뵽����
    IEnumerator SaveFile(string savePath,byte[] bytes, Action callBack)
    {
        FileStream fileStream = File.Open(savePath, FileMode.OpenOrCreate);

        yield return fileStream.WriteAsync(bytes, 0, bytes.Length);

        // �ͷ��ļ����������ļ���һֱ���ڶ�ȡ��״̬�����ܱ��������̶�ȡ
        fileStream.Flush();
        fileStream.Close();
        fileStream.Dispose();

        callBack?.Invoke();
        Debug.Log($"{savePath}�ļ��������");

    }

    void CheckAssetBundlePattern() // Զ��remote
    {
        if (LoadPattern == AssetBundlePattern.Remote)
        {
            StartCoroutine(DownloadFile("", LoadAssetBundle));
        }
        else
        {
            LoadAssetBundle();
        }
    }

    // �����ť������AB��
    void LoadAssetBundle()
    {
        // ����AB����·��������������AB����·��һ��
        string assetBundlePath = Path.Combine(AssetBundleLoadPath, "");
        Debug.Log($"����AB����·��Ϊ��{assetBundlePath}");
        // C:/Users/wyh/AppData/LocalLow/DefaultCompany/Test\DownloadAssetBundle
        //                                 \SampleAssetBundle\SampleAssetBundle
        
        // ͨ���嵥�����(����)��Manifest,����������
        AssetBundle mainAB = AssetBundle.LoadFromFile(assetBundlePath); // ���ļ����м���AB��
        
        // manifest�ļ�����ʵ���������Ĵ洢�����ǿ����߲���������
        AssetBundleManifest mainManifest = mainAB.LoadAsset<AssetBundleManifest>(nameof(AssetBundleManifest));

        // ��ʵ��������֮ǰ,�����������������ص��ڴ���
        // �� .manifest�л�ȡassetBundleName��������������AB���İ�����ͬ���˴�����Ϊ0
        foreach (var depBundleName in mainManifest.GetAllDependencies("0"))
        {
            // ��ȡ Assets/ ...\SampleAssetBundle\���������ƣ��ܵķ��򻹲�һ��
            assetBundlePath = Path.Combine(AssetBundleLoadPath, depBundleName);

            Debug.Log(assetBundlePath);
            AssetBundle.LoadFromFile(assetBundlePath); // ��Ϊֻ����������Բ��ñ�������
            
        }

        // ���ļ����л�ȡAB��������AB���İ���Ϊ 0 (��Ҫ��¡�������AB��)
        assetBundlePath = Path.Combine(AssetBundleLoadPath, "0");
        CubeBundle = AssetBundle.LoadFromFile(assetBundlePath);

        assetBundlePath = Path.Combine(AssetBundleLoadPath, "1");
        SphereBundle = AssetBundle.LoadFromFile(assetBundlePath); 
    }

    // �����ť��������Դ
    void LoadAsset()
    {
        // ��AB���м�����Դ���󣬲�ʵ����, Ԥ���������Ϊ Cube
        GameObject cubeObject = CubeBundle.LoadAsset<GameObject>("Cube");
        CubeObject = Instantiate(cubeObject); // ʵ��������

        GameObject sphereObject = SphereBundle.LoadAsset<GameObject>("Sphere");
        SphereObject = Instantiate(sphereObject); // ʵ��������
    }

    // �����ť��ж����Դ
    void UnLoadAsset(bool isTrue)
    {
        // unload(true)ʱ����ǿ�ƴ��ڴ��л�����Դ���Ͷ��󣬵��޷�������Ϸ����
        if (isTrue) // isTrueΪtrueʱ��ִ��
        {
            // ǿ�ƻ���AssetBundle�����е���Դ����ʵ���������廹���ڣ�ֻ�������ö�ʧ��
            CubeBundle.Unload(isTrue); // true
            SphereBundle.Unload(isTrue);
        }
        else
        {
            // ɾ�������е�ʵ���Լ��ٶ���Դ������
            DestroyImmediate(CubeObject); // ��������
            DestroyImmediate(SphereObject);

            CubeBundle.Unload(isTrue); // false
            SphereBundle.Unload(isTrue);

            // ���ڴ���ж�ش�δʹ�ù�����Դ
            Resources.UnloadUnusedAssets();
        }
    }
}
