﻿using JsonModels;
using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// CheckboxQuestion allows to menage a list of checkboxes plus a submit button. The exclusive flag
/// imposes to have at most a selected box. The compulsory flag imposes to have at least a selected
/// box.
/// </summary>
public class CheckboxQuestion : MonoBehaviour {

    [SerializeField] private int questionId;
    [SerializeField] private Option[] options;
    [SerializeField] private Button submit;
    [SerializeField] private bool exclusive;
    [SerializeField] private bool compulsory;

    private int activeCount = 0;

    private void Start() {
        if (!compulsory)
            submit.interactable = true;
    }

    // Updates the active answers.
    public void UpdateAnswer(int id) {
        bool activated = GetOptionById(id).toggle.isOn;

        if (exclusive && activated) {
            foreach (Option o in options) {
                if (o.id != id && o.toggle.isOn) {
                    // Updating the isOn value calls again this method, so there is non need
                    // to decrease the active count now.
                    o.toggle.isOn = false;
                }
            }
        }

        activeCount = activated ? activeCount + 1 : activeCount - 1;
        submit.interactable = activeCount > 0 || !compulsory ? true : false;
    }

    // Returns the oprion given its id.
    private Option GetOptionById(int id) {
        foreach (Option o in options) {
            if (o.id == id) {
                return o;
            }
        }
        return new Option();
    }

    // Returns the active answers as an array.
    private int[] GetActiveAnswers() {
        int[] activeAnswers = new int[activeCount];

        int j = 0;
        for (int i = 0; i < options.Length; i++) {
            if (options[i].toggle.isOn) {
                activeAnswers[j] = options[i].id;
                j++;
            }
        }

        return activeAnswers;
    }

    // Converts the answe in Json format.
    public string GetJsonAnswer() {
        return JsonUtility.ToJson(new JsonAnswer {
            questionId = questionId,
            answers = GetActiveAnswers()
        });
    }

    // Converts the question and the options in Json format.
    public string GetJsonQuestion() {
        string jq = JsonUtility.ToJson(new JsonQuestion {
            questionId = questionId,
            questionText = transform.GetComponentInChildren<Text>().text,
            options = ""
        });
        return jq.Remove(jq.Length - 3) + GetJsonOptions() + "}";
    }

    // Converts the options in Json format.
    private string GetJsonOptions() {
        string jOptions = "[";

        for (int i = 0; i < options.Length; i++) {
            jOptions += JsonUtility.ToJson(new JsonOption {
                optionId = (int)options[i].id,
                optionText = options[i].toggle.transform.parent.GetComponentInChildren<Text>().text
            });
            if (i < options.Length - 1) {
                jOptions += ", ";
            }
        }

        return jOptions + "]";
    }

    [Serializable]
    private struct Option {
        public Toggle toggle;
        public int id;
    }

}