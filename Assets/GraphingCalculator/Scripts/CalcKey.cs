using UdonSharp;
using UnityEngine;

namespace GraphCalc
{
    /// <summary>
    /// Forwards a UI event (button click, slider change, input field edit, scroll) to the calculator.
    /// Wired by the builder: UnityEvent -> UdonBehaviour.SendCustomEvent("OnPress").
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class CalcKey : UdonSharpBehaviour
    {
        public GraphCalculator calc;
        public string key;

        public void OnPress()
        {
            if (calc != null) calc.OnKey(key);
        }
    }
}
