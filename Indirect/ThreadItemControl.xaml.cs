﻿using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.Contacts;
using Windows.ApplicationModel.DataTransfer;
using Windows.Media.Core;
using Windows.Media.Streaming.Adaptive;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Indirect.Wrapper;
using InstagramAPI.Classes.Direct;
using InstagramAPI.Classes.Media;
using Microsoft.Toolkit.Uwp.UI;

// The User Control item template is documented at https://go.microsoft.com/fwlink/?LinkId=234236

namespace Indirect
{
    internal sealed partial class ThreadItemControl : UserControl
    {
        // public static readonly DependencyProperty ThreadProperty = DependencyProperty.Register(
        //     nameof(Thread),
        //     typeof(InstaDirectInboxThreadWrapper),
        //     typeof(ThreadItemControl),
        //     new PropertyMetadata(null, OnThreadChanged));

        public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
            nameof(Item),
            typeof(InstaDirectInboxItemWrapper),
            typeof(ThreadItemControl),
            new PropertyMetadata(null, OnItemSourceChanged));

        public InstaDirectInboxItemWrapper Item
        {
            get => (InstaDirectInboxItemWrapper) GetValue(ItemProperty);
            set => SetValue(ItemProperty, value);
        }

        // public InstaDirectInboxThreadWrapper Thread
        // {
        //     get => (InstaDirectInboxThreadWrapper) GetValue(ThreadProperty);
        //     set => SetValue(ThreadProperty, value);
        // }

        private static void OnThreadChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var view = (ThreadItemControl)d;
        }

        private static void OnItemSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var view = (ThreadItemControl) d;
            var item = (InstaDirectInboxItemWrapper) e.NewValue;
            view.ProcessItem();
            if (item.ItemType == DirectItemType.ActionLog)
                view.ItemContainer.Visibility = Visibility.Collapsed;
            if (item.ItemType == DirectItemType.Text)
                view.MenuCopyOption.Visibility = Visibility.Visible;
        }

        public ThreadItemControl()
        {
            this.InitializeComponent();
        }

        private void ProcessItem()
        {
            Item.Timestamp = Item.Timestamp.ToLocalTime();
            if (Item.ItemType == DirectItemType.Link)
                Item.Text = Item.Link.Text;
        }

        private void ShowTimestamp(object sender, DoubleTappedRoutedEventArgs e)
        {
            var panel = (Panel)sender;
            var timestampTextBlock = panel.Children.Last();
            timestampTextBlock.Visibility = timestampTextBlock.Visibility == Visibility.Collapsed ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ImageFrame_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (Item.ItemType == DirectItemType.AnimatedMedia) return;
            var uri = Item.FullImageUri;
            if (uri == null) return;
            var immersive = new ImmersiveView(Item, InstaMediaType.Image);
            var result = immersive.ShowAsync();
        }

        private void VideoPopupButton_OnTapped(object sender, TappedRoutedEventArgs e)
        {
            var uri = Item.VideoUri;
            if (uri == null) return;
            var immersive = new ImmersiveView(Item, InstaMediaType.Video);
            var result = immersive.ShowAsync();
        }

        private void OpenMediaButton_OnClick(object sender, RoutedEventArgs e)
        {
            ImageFrame_Tapped(sender, new TappedRoutedEventArgs());
        }

        private void OpenWebLink(object sender, TappedRoutedEventArgs e)
        {
            if (Item.NavigateUri == null) return;
            _ = Windows.System.Launcher.LaunchUriAsync(Item.NavigateUri);
        }

        private void ReelShareImage_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (Item.ReelShareMedia.Media.MediaType == 1)
            {
                ImageFrame_Tapped(sender, e);
            }
            else
            {
                VideoPopupButton_OnTapped(sender, e);
            }
        }

        private bool _timeout;
        private async void LikeUnlike_Click(object sender, RoutedEventArgs e)
        {
            if (_timeout) return;
            if (Item.Reactions?.MeLiked ?? false)
            {
                Item.UnlikeItem();
            }
            else
            {
                Item.LikeItem();
            }
            _timeout = true;
            await Task.Delay(TimeSpan.FromSeconds(2));
            _timeout = false;

        }

        private void MenuCopyOption_Click(object sender, RoutedEventArgs e)
        {
            var border = MainContentControl.ContentTemplateRoot as Border;
            var textBlock = border?.Child as TextBlock;
            if (textBlock == null) return;
            var dataPackage = new DataPackage();
            dataPackage.RequestedOperation = DataPackageOperation.Copy;
            dataPackage.SetText(textBlock.Text);
            Clipboard.SetContent(dataPackage);
        }
    }
}
