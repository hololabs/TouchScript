﻿/*
 * @author Valentin Simonov / http://va.lent.in/
 */

using System;
using System.Collections.Generic;
using TUIOsharp;
using TUIOsharp.DataProcessors;
using TUIOsharp.Entities;
using UnityEngine;

namespace TouchScript.InputSources
{
    /// <summary>
    /// Processes TUIO 1.1 input.
    /// </summary>
    [AddComponentMenu("TouchScript/Input Sources/TUIO Input")]
    public sealed class TuioInput : InputSource
    {
        #region Constants

        [Flags]
        public enum InputType
        {
            Cursors = 1 << 0,
            Blobs = 1 << 1,
            Objects = 1 << 2
        }

        #endregion

        #region Public properties

        /// <summary>
        /// Port to listen to.
        /// </summary>
        public int TuioPort
        {
            get { return tuioPort; }
            set
            {
                if (tuioPort == value) return;
                tuioPort = value;
                connect();
            }
        }

        public InputType SupportedInputs
        {
            get { return supportedInputs; }
            set
            {
                if (supportedInputs == value) return;
                supportedInputs = value;
                updateInputs();
            }
        }

        public List<TuioObjectMapping> TuioObjectMappings { get { return tuioObjectMappings; } }

        public Tags CursorTags { get { return cursorTags; }}
        public Tags BlobTags { get { return blobTags; }}
        public Tags ObjectTags { get { return objectTags; } }

        #endregion

        #region Private variables

        [SerializeField]
        private int tuioPort = 3333;
        [SerializeField]
        private InputType supportedInputs = InputType.Cursors | InputType.Blobs | InputType.Objects;

        [SerializeField]
        private List<TuioObjectMapping> tuioObjectMappings = new List<TuioObjectMapping>();
        [SerializeField]
        private Tags cursorTags  = new Tags(new List<string>() {Tags.SOURCE_TUIO, Tags.INPUT_TOUCH});
        [SerializeField]
        private Tags blobTags = new Tags(new List<string>() {Tags.SOURCE_TUIO, Tags.INPUT_TOUCH});
        [SerializeField]
        private Tags objectTags = new Tags(new List<string>() {Tags.SOURCE_TUIO, Tags.INPUT_OBJECT});

        private TuioServer server;
        private CursorProcessor cursorProcessor;
        private ObjectProcessor objectProcessor;
        private BlobProcessor blobProcessor;

        private Dictionary<TuioCursor, ITouch> cursorToInternalId = new Dictionary<TuioCursor, ITouch>();
        private Dictionary<TuioBlob, ITouch> blobToInternalId = new Dictionary<TuioBlob, ITouch>();
        private Dictionary<TuioObject, ITouch> objectToInternalId = new Dictionary<TuioObject, ITouch>();
        private int screenWidth;
        private int screenHeight;

        #endregion

        #region Unity

        /// <inheritdoc />
        protected override void OnEnable()
        {
            base.OnEnable();

            cursorProcessor = new CursorProcessor();
            cursorProcessor.CursorAdded += OnCursorAdded;
            cursorProcessor.CursorUpdated += OnCursorUpdated;
            cursorProcessor.CursorRemoved += OnCursorRemoved;

            blobProcessor = new BlobProcessor();
            blobProcessor.BlobAdded += OnBlobAdded;
            blobProcessor.BlobUpdated += OnBlobUpdated;
            blobProcessor.BlobRemoved += OnBlobRemoved;

            objectProcessor = new ObjectProcessor();
            objectProcessor.ObjectAdded += OnObjectAdded;
            objectProcessor.ObjectUpdated += OnObjectUpdated;
            objectProcessor.ObjectRemoved += OnObjectRemoved;           

            connect();
        }

        /// <inheritdoc />
        protected override void Update()
        {
            base.Update();
            screenWidth = Screen.width;
            screenHeight = Screen.height;
        }

        /// <inheritdoc />
        protected override void OnDisable()
        {
            disconnect();
            base.OnDisable();
        }

        #endregion

        #region Private functions

        private void connect()
        {
            if (!Application.isPlaying) return;
            if (server != null) disconnect();

            server = new TuioServer(TuioPort);
            server.Connect();
            updateInputs();
        }

        private void disconnect()
        {
            if (server != null)
            {
                server.RemoveAllDataProcessors();
                server.Disconnect();
                server = null;
            }

            foreach (var i in cursorToInternalId)
            {
                cancelTouch(i.Value.Id);
            }
        }

        private void updateInputs()
        {
            if (server == null) return;

            if ((supportedInputs & InputType.Cursors) != 0) server.AddDataProcessor(cursorProcessor);    
            else server.RemoveDataProcessor(cursorProcessor);
            if ((supportedInputs & InputType.Blobs) != 0) server.AddDataProcessor(blobProcessor);
            else server.RemoveDataProcessor(blobProcessor);
            if ((supportedInputs & InputType.Objects) != 0) server.AddDataProcessor(objectProcessor);
            else server.RemoveDataProcessor(objectProcessor);
        }

        private void updateBlobProperties(ITouch touch, TuioBlob blob)
        {
            var props = touch.Properties;

            props["Angle"] = blob.Angle;
            props["Width"] = blob.Width;
            props["Height"] = blob.Height;
            props["Area"] = blob.Area;
            props["RotationVelocity"] = blob.RotationVelocity;
            props["RotationAcceleration"] = blob.RotationAcceleration;
        }

        private void updateObjectProperties(ITouch touch, TuioObject obj)
        {
            var props = touch.Properties;

            props["Angle"] = obj.Angle;
            props["ObjectId"] = obj.ClassId;
            props["RotationVelocity"] = obj.RotationVelocity;
            props["RotationAcceleration"] = obj.RotationAcceleration;
        }

        private string getTagById(int id)
        {
            foreach (var tuioObjectMapping in tuioObjectMappings)
            {
                if (tuioObjectMapping.Id == id) return tuioObjectMapping.Tag;
            }
            return null;
        }

        #endregion

        #region Event handlers

        private void OnCursorAdded(object sender, TuioCursorEventArgs e)
        {
            var entity = e.Cursor;
            lock (this)
            {
                var x = entity.X * screenWidth;
                var y = (1 - entity.Y) * screenHeight;
                cursorToInternalId.Add(entity, beginTouch(new Vector2(x, y), new Tags(CursorTags)));
            }
        }

        private void OnCursorUpdated(object sender, TuioCursorEventArgs e)
        {
            var entity = e.Cursor;
            lock (this)
            {
                ITouch touch;
                if (!cursorToInternalId.TryGetValue(entity, out touch)) return;

                var x = entity.X * screenWidth;
                var y = (1 - entity.Y) * screenHeight;

                updateTouch(touch.Id, new Vector2(x, y));
            }
        }

        private void OnCursorRemoved(object sender, TuioCursorEventArgs e)
        {
            var entity = e.Cursor;
            lock (this)
            {
                ITouch touch;
                if (!cursorToInternalId.TryGetValue(entity, out touch)) return;

                cursorToInternalId.Remove(entity);
                endTouch(touch.Id);
            }
        }

        private void OnBlobAdded(object sender, TuioBlobEventArgs e)
        {
            var entity = e.Blob;
            lock (this)
            {
                var x = entity.X * screenWidth;
                var y = (1 - entity.Y) * screenHeight;
                var touch = beginTouch(new Vector2(x, y), new Tags(BlobTags));
                updateBlobProperties(touch, entity);
                blobToInternalId.Add(entity, touch);
            }
        }

        private void OnBlobUpdated(object sender, TuioBlobEventArgs e)
        {
            var entity = e.Blob;
            lock (this)
            {
                ITouch touch;
                if (!blobToInternalId.TryGetValue(entity, out touch)) return;

                var x = entity.X * screenWidth;
                var y = (1 - entity.Y) * screenHeight;

                updateTouch(touch.Id, new Vector2(x, y));
                updateBlobProperties(touch, entity);
            }
        }

        private void OnBlobRemoved(object sender, TuioBlobEventArgs e)
        {
            var entity = e.Blob;
            lock (this)
            {
                ITouch touch;
                if (!blobToInternalId.TryGetValue(entity, out touch)) return;

                blobToInternalId.Remove(entity);
                endTouch(touch.Id);
            }
        }

        private void OnObjectAdded(object sender, TuioObjectEventArgs e)
        {
            var entity = e.Object;
            lock (this)
            {
                var x = entity.X * screenWidth;
                var y = (1 - entity.Y) * screenHeight;
                var touch = beginTouch(new Vector2(x, y), new Tags(ObjectTags));
                updateObjectProperties(touch, entity);
                objectToInternalId.Add(entity, touch);
                touch.Tags.AddTag(getTagById(entity.ClassId));
            }
        }

        private void OnObjectUpdated(object sender, TuioObjectEventArgs e)
        {
            var entity = e.Object;
            lock (this)
            {
                ITouch touch;
                if (!objectToInternalId.TryGetValue(entity, out touch)) return;

                var x = entity.X * screenWidth;
                var y = (1 - entity.Y) * screenHeight;

                updateTouch(touch.Id, new Vector2(x, y));
                updateObjectProperties(touch, entity);
            }
        }

        private void OnObjectRemoved(object sender, TuioObjectEventArgs e)
        {
            var entity = e.Object;
            lock (this)
            {
                ITouch touch;
                if (!objectToInternalId.TryGetValue(entity, out touch)) return;

                objectToInternalId.Remove(entity);
                endTouch(touch.Id);
            }
        }

        #endregion
    }

    [Serializable]
    public class TuioObjectMapping
    {
        public int Id;
        public string Tag;
    }

}