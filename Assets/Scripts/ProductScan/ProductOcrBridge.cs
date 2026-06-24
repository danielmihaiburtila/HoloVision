using System;
using System.Collections;
using UnityEngine;

public abstract class ProductOcrBridge : MonoBehaviour
{
    public abstract IEnumerator ReadTextFromJpg(byte[] jpgBytes, Action<string> onDone);
}