using System;
using UnityEngine;
using UnityEngine.UI;

namespace Foundation
{
    /// <summary>Lets a stepper row re-read its value text without being rebuilt.</summary>
    public class StepperRefresher : MonoBehaviour
    {
        public Text Label;
        public Func<string> GetText;

        public void Refresh()
        {
            if (Label != null && GetText != null) Label.text = GetText();
        }
    }
}
