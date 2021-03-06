﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InstagramAPI.Utils
{
    public class PersistentDictionary<TValue> : Dictionary<string, TValue>
    {
        public string Identifier { get; set; }

        private readonly Windows.Storage.ApplicationDataContainer _localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;

        public PersistentDictionary(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) throw new ArgumentNullException(nameof(identifier));
            Identifier = identifier;
        }

        public void SaveToAppSettings()
        {
            var composite = new Windows.Storage.ApplicationDataCompositeValue();
            foreach (var (key, value) in this)
            {
                composite[key] = value;
            }
            _localSettings.Values[Identifier] = composite;
        }

        public void LoadFromAppSettings()
        {
            var composite = (Windows.Storage.ApplicationDataCompositeValue)_localSettings.Values[Identifier];
            if (composite == null) return;
            foreach (var (key, value) in composite)
            {
                this[key] = (TValue) value;
            }
        }

        public void RemoveFromAppSettings()
        {
            _localSettings.Values.Remove(Identifier);
        }
    }
}
