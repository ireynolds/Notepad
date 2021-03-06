﻿// Copyright (C) Isaac Reynolds. All Rights Reserved.
// This code released under the terms of the Microsoft Public License
// (Ms-PL, http://opensource.org/licenses/ms-pl.html).

using System;
using NotepadTheNextVersion.Enumerations;
using System.Windows.Navigation;
using NotepadTheNextVersion.Utilities;
using System.IO.IsolatedStorage;
using NotepadTheNextVersion.Exceptions;
using Microsoft.Phone.Shell;
using System.Linq;
using System.Windows;
using System.Collections.ObjectModel;
using Microsoft.Phone.Controls;

namespace NotepadTheNextVersion.Models
{
    public class Directory : IActionable, IComparable<Directory>
    {
        private PathStr _path;


        private bool _isTemp;

        public bool IsFavorite
        {
            get
            {
                var favs = SettingUtils.GetSetting<Collection<string>>(Setting.FavoritesList);
                return favs.Contains(this.Path.PathString);
            }
            set
            {
                var favs = SettingUtils.GetSetting<Collection<string>>(Setting.FavoritesList);
                if (value && !IsFavorite)
                    favs.Add(this.Path.PathString);
                else if (!value && IsFavorite)
                    favs.Remove(this.Path.PathString);
                App.AppSettings.Save();
            }
        }

        public bool IsTemp
        {
            get
            {
                return _isTemp;
            }
            set
            {
                _isTemp = value;
            }
        }

        private bool isTrash
        {
            get
            {
                return _path.IsInTrash;
            }
        }

        public string Name
        {
            get { return _path.Name; }
        }

        public string DisplayName
        {
            get { return _path.DisplayName; }
        }

        public bool IsPinned
        {
            get 
            {
                return Utils.GetTile(Path.PathString) != null; 
            }
        }

        public PathStr Path
        {
            get { return new PathStr(_path); }
        }

        public Directory(PathStr p)
        {
            // This is code to fix my big bug
            if (!FileUtils.IsDir(p.PathString))
            {
                // throw new Exception();
                if (!FileUtils.IsDoc(p.PathString))
                {
                    using (IsolatedStorageFile isf = IsolatedStorageFile.GetUserStoreForApplication())
                    {
                        var nn = p.PathString + FileUtils.DIRECTORY_EXTENSION;
                        isf.MoveDirectory(p.PathString, nn);
                    }
                    p = new PathStr(p.PathString + FileUtils.DIRECTORY_EXTENSION);
                }
            }
            _path = p;
        }

        public Directory(Directory parent, string name)
        {
            if (!FileUtils.IsDir(name))
                throw new Exception();
            _path = parent.Path.NavigateIn(name, ItemType.Directory);
        }

        public Directory(PathBase Base)
            : this(new PathStr(Base)) { }

        public void Open(NavigationService NavigationService)
        {
            NavigationService.Navigate(App.Listings.AddArg(this));
        }

        public void NavToMove(NavigationService NavigationService)
        {
            NavigationService.Navigate(App.MoveItem.AddArg(this));
        }

        public IActionable Move(Directory newParent) 
        {
            var newLocation = new Directory(newParent.Path.NavigateIn(Name));
            if (FileUtils.DirectoryExists(newLocation.Path.PathString))
            {
                MessageBox.Show("A directory with the specified name already exists.", "An error occurred", MessageBoxButton.OK);
                return null;
            }
            
            try
            {
                FileUtils.MoveDirectory(Path.PathString, newLocation.Path.PathString);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Document Store could not move the directory. There may be an existing directory at the specified destination.", "An error occurred", MessageBoxButton.OK);
                return null;
            }
            if (IsFavorite)
            {
                this.IsFavorite = false;
                if (!newLocation.isTrash)
                    newLocation.IsFavorite = true;
            }
            if (IsPinned)
                TogglePin();
            return newLocation;
        }

        public void NavToRename(NavigationService NavigationService, PhoneApplicationPage page)
        {
            NavigationService.Navigate(App.RenameItem.AddArg(this)
                                                     .AddArg("istemp", IsTemp.ToString())
                                                     .AddArg("prevpage", page.NavigationService.CurrentSource.OriginalString));
        }

        public IActionable Rename(string newDirectoryName)
        {
            PathStr newLocation = Path.Parent.NavigateIn(newDirectoryName, ItemType.Directory);
            if (FileUtils.DirectoryExists(newLocation.PathString))
            {
                MessageBox.Show("A directory with the specified name already exists.", "An error occurred", MessageBoxButton.OK);
                return null;
            }

            try
            {
                FileUtils.MoveDirectory(Path.PathString, newLocation.PathString);
            }
            catch (IsolatedStorageException ex)
            {
                MessageBox.Show("Document Store could not rename the directory. There may be illegal characters in the specified name.\n\nIf applicable, remove any special characters or punctuation in the name.", "An error occurred", MessageBoxButton.OK);
                return null;
            }
            Directory newDir = new Directory(newLocation);
            if (IsFavorite)
                FileUtils.ReplaceFavorite(this, newDir);
            if (IsPinned)
                TogglePin();
            return newDir;
        }

        public IActionable Delete(bool permanently = false)
        {
            if (isTrash || permanently)
            {
                using (var isf = IsolatedStorageFile.GetUserStoreForApplication())
                {
                    DeleteRecursive(this.Path, isf);
                }
                return null;
            }
            else // if (!isTrash)
            {
                Directory trash = new Directory(new PathStr(PathBase.Trash));
                Directory newLoc = new Directory(trash.Path.NavigateIn(Name, ItemType.Default));
                if (newLoc.Exists())
                    newLoc.Delete();

                ApplyRecursive(a =>
                {
                    if (a.IsPinned)
                        a.TogglePin();
                    if (a.IsFavorite)
                        a.IsFavorite = false;
                });
                try
                {
                    this.Move(trash);
                }
                catch (Exception ex)
                {
                    return null;
                }
                this.IsFavorite = false;
                if (IsPinned)
                    TogglePin();
                return newLoc;
            }
        }

        public void TogglePin()
        {
            // Import System.Linq to use "extension" methods
            ShellTile currTile = Utils.GetTile(Path.PathString);

            if (currTile == null)
            {
                StandardTileData data = new StandardTileData();
                data.Title = this.DisplayName;
                data.BackgroundImage = new Uri(App.DirectoryTile, UriKind.Relative);
                Uri myUri = App.Listings + "?param=" + Uri.EscapeDataString(Path.PathString); // App.Listings already has ?id= attached in order to create a unique string
                ShellTile.Create(myUri, data);
            }
            else
            {
                currTile.Delete();
            }
        }

        public bool Exists()
        {
            return FileUtils.DirectoryExists(Path.PathString);
        }

        public IActionable SwapRoot()
        {
            Directory d = new Directory(Path.UpdateRoot());
            if (this.IsFavorite)
            {
                this.IsFavorite = false;
                d.IsFavorite = true;
            }
            return d;
        }

        public int CompareTo(IActionable other)
        {
            if (other.GetType() == typeof(Document))
                return -1;
            else
                return this.Name.CompareTo(other.Name);
        }

        public int CompareTo(Directory other)
        {
            return CompareTo((IActionable)other);
        }

        #region Private Helpers

        private void ApplyRecursive(Action<IActionable> a)
        {
            foreach (var d in FileUtils.GetAllDocuments(this))
                a(d);
            foreach (var d in FileUtils.GetAllDirectories(this))
                a(d);
        }

        private static void DeleteRecursive(PathStr dir, IsolatedStorageFile isf)
        {
            // Delete every subdirectory's contents recursively
            foreach (string subDir in isf.GetDirectoryNames(dir.PathString + "/*"))
                DeleteRecursive(dir.NavigateIn(subDir, ItemType.Default), isf);
            // Delete every file inside
            foreach (string file in isf.GetFileNames(dir.PathString + "/*"))
                isf.DeleteFile(System.IO.Path.Combine(dir.PathString, file));

            isf.DeleteDirectory(dir.PathString);
        }

        #endregion
    }
}
