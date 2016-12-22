﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using AppKit;
using CoreAnimation;
using Foundation;
using Xamarin.Forms.Internals;

namespace Xamarin.Forms.Platform.MacOS
{
	public class NavigationPageRenderer : NSViewController, IVisualElementRenderer, IEffectControlProvider
	{
		bool _disposed;
		bool _appeared;
		string _previousTitle;
		string _currentTitle;
		EventTracker _events;
		VisualElementTracker _tracker;
		Stack<PageWrapper> _currentStack = new Stack<PageWrapper>();

		IPageController PageController => Element as IPageController;

		IElementController ElementController => Element as IElementController;

		INavigationPageController NavigationController => Element as INavigationPageController;

		void IEffectControlProvider.RegisterEffect(Effect effect)
		{
			var platformEffect = effect as PlatformEffect;
			if (platformEffect != null)
				platformEffect.Container = View;
		}

		public NavigationPageRenderer() : this(IntPtr.Zero) { }
		public NavigationPageRenderer(IntPtr handle)
		{
			View = new FormsNSView { WantsLayer = true };
		}

		public VisualElement Element { get; private set; }

		public event EventHandler<VisualElementChangedEventArgs> ElementChanged;

		public SizeRequest GetDesiredSize(double widthConstraint, double heightConstraint)
		{
			return NativeView.GetSizeRequest(widthConstraint, heightConstraint);
		}

		public NSViewController ViewController => this;

		public NSView NativeView => View;

		public void SetElement(VisualElement element)
		{
			var oldElement = Element;
			Element = element;

			Init();

			OnElementChanged(new VisualElementChangedEventArgs(oldElement, element));

			EffectUtilities.RegisterEffectControlProvider(this, oldElement, element);
		}

		public void SetElementSize(Size size)
		{
			Element.Layout(new Rectangle(Element.X, Element.Y, size.Width, size.Height));
		}

		public Task<bool> PopToRootAsync(Page page, bool animated = true)
		{
			return OnPopToRoot(page, animated);
		}

		public Task<bool> PopViewAsync(Page page, bool animated = true)
		{
			return OnPop(page, animated);
		}

		public Task<bool> PushPageAsync(Page page, bool animated = true)
		{
			return OnPush(page, animated);
		}

		protected override void Dispose(bool disposing)
		{
			if (!_disposed && disposing)
			{
				if (Element != null)
				{
					PageController?.SendDisappearing();
					((Element as IPageContainer<Page>)?.CurrentPage as IPageController)?.SendDisappearing();
					Element.PropertyChanged -= HandlePropertyChanged;
					Element = null;
				}

				_tracker?.Dispose();
				_tracker = null;

				_events?.Dispose();
				_events = null;

				_disposed = true;
			}
			base.Dispose(disposing);
		}

		public override void RemoveFromParentViewController()
		{


			base.RemoveFromParentViewController();
		}

		public override void ViewDidDisappear()
		{
			base.ViewDidDisappear();
			if (!_appeared)
				return;
			Platform.NativeToolbarTracker.TryHide(Element as NavigationPage);
			_appeared = false;
			PageController?.SendDisappearing();
		}

		public override void ViewDidAppear()
		{
			base.ViewDidAppear();
			Platform.NativeToolbarTracker.Navigation = (NavigationPage)Element;
			if (_appeared)
				return;

			_appeared = true;
			PageController?.SendAppearing();
		}

		protected virtual void OnElementChanged(VisualElementChangedEventArgs e)
		{
			if (e.OldElement != null)
				e.OldElement.PropertyChanged -= HandlePropertyChanged;

			if (e.NewElement != null)
				e.NewElement.PropertyChanged += HandlePropertyChanged;

			ElementChanged?.Invoke(this, e);
		}

		protected virtual void ConfigurePageRenderer()
		{
			View.WantsLayer = true;
		}

		//TODO: Implement PopToRoot
		protected virtual async Task<bool> OnPopToRoot(Page page, bool animated)
		{
			var renderer = Platform.GetRenderer(page);
			if (renderer == null || renderer.ViewController == null)
				return false;

			var success = false;

			Platform.NativeToolbarTracker.UpdateToolbarItems();
			return success;
		}

		protected virtual async Task<bool> OnPop(Page page, bool animated)
		{
			var removed = await PopPageAsync(page, animated);
			Platform.NativeToolbarTracker.UpdateToolbarItems();
			return removed;
		}

		protected virtual async Task<bool> OnPush(Page page, bool animated)
		{
			var shown = await AddPage(page, animated);
			Platform.NativeToolbarTracker.UpdateToolbarItems();
			return shown;
		}

		void Init()
		{
			ConfigurePageRenderer();

			var navPage = (NavigationPage)Element;

			if (navPage.CurrentPage == null)
				throw new InvalidOperationException("NavigationPage must have a root Page before being used. Either call PushAsync with a valid Page, or pass a Page to the constructor before usage.");

			Platform.NativeToolbarTracker.Navigation = navPage;

			NavigationController.PushRequested += OnPushRequested;
			NavigationController.PopRequested += OnPopRequested;
			NavigationController.PopToRootRequested += OnPopToRootRequested;
			NavigationController.RemovePageRequested += OnRemovedPageRequested;
			NavigationController.InsertPageBeforeRequested += OnInsertPageBeforeRequested;

			UpdateBarBackgroundColor();
			UpdateBarTextColor();

			_events = new EventTracker(this);
			_events.LoadEvents(NativeView);
			_tracker = new VisualElementTracker(this);


			((INavigationPageController)navPage).StackCopy.Reverse().ForEach(async p => await PushPageAsync(p, false));

			UpdateBackgroundColor();
		}

		IVisualElementRenderer CreateViewControllerForPage(Page page)
		{
			if (Platform.GetRenderer(page) == null)
				Platform.SetRenderer(page, Platform.CreateRenderer(page));

			var pageRenderer = Platform.GetRenderer(page);
			return pageRenderer;
		}

		void InsertPageBefore(Page page, Page before)
		{
			if (before == null)
				throw new ArgumentNullException(nameof(before));
			if (page == null)
				throw new ArgumentNullException(nameof(page));

		}

		void OnInsertPageBeforeRequested(object sender, NavigationRequestedEventArgs e)
		{
			InsertPageBefore(e.Page, e.BeforePage);
		}

		void OnPopRequested(object sender, NavigationRequestedEventArgs e)
		{
			e.Task = PopViewAsync(e.Page, e.Animated);
		}

		void OnPopToRootRequested(object sender, NavigationRequestedEventArgs e)
		{
			e.Task = PopToRootAsync(e.Page, e.Animated);
		}

		void OnPushRequested(object sender, NavigationRequestedEventArgs e)
		{
			e.Task = PushPageAsync(e.Page, e.Animated);
		}

		void OnRemovedPageRequested(object sender, NavigationRequestedEventArgs e)
		{
			RemovePage(e.Page, true);
			Platform.NativeToolbarTracker.UpdateToolbarItems();
		}

		void RemovePage(Page page, bool removeFromStack)
		{
			(page as IPageController)?.SendDisappearing();
			var target = Platform.GetRenderer(page);
			target?.NativeView?.RemoveFromSuperview();
			target?.ViewController?.RemoveFromParentViewController();
			target?.Dispose();
			if (removeFromStack)
			{
				var newStack = new Stack<PageWrapper>();
				foreach (var stack in _currentStack)
				{
					if (stack.Page != page)
					{
						newStack.Push(stack);
					}
				}
				_currentStack = newStack;
			}
		}

		async Task<bool> PopPageAsync(Page page, bool animated)
		{
			if (page == null)
				throw new ArgumentNullException(nameof(page));

			var wrapper = _currentStack.Peek();
			if (page != wrapper.Page)
				throw new NotSupportedException("Popped page does not appear on top of current navigation stack, please file a bug.");

			_currentStack.Pop();
			(page as IPageController)?.SendDisappearing();

			var target = Platform.GetRenderer(page);
			var previousPage = _currentStack.Peek().Page;

			if (animated)
			{
				var previousPageRenderer = Platform.GetRenderer(previousPage);
				return await this.HandleAsyncAnimation(target.ViewController, previousPageRenderer.ViewController, NSViewControllerTransitionOptions.SlideBackward, () => Platform.DisposeRendererAndChildren(target), true);
			}

			RemovePage(page, false);
			return true;
		}

		async Task<bool> AddPage(Page page, bool animated)
		{
			if (page == null)
				throw new ArgumentNullException(nameof(page));

			Page oldPage = null;
			if (_currentStack.Count >= 1)
				oldPage = _currentStack.Peek().Page;

			_currentStack.Push(new PageWrapper(page));

			var vc = CreateViewControllerForPage(page);
			vc.SetElementSize(new Size(View.Bounds.Width, View.Bounds.Height));
			page.Layout(new Rectangle(0, 0, View.Bounds.Width, View.Frame.Height));

			if (_currentStack.Count == 1 || !animated)
			{
				vc.NativeView.WantsLayer = true;
				AddChildViewController(vc.ViewController);
				View.AddSubview(vc.NativeView);
				return true;
			}
			var vco = Platform.GetRenderer(oldPage);
			AddChildViewController(vc.ViewController);
			return await this.HandleAsyncAnimation(vco.ViewController, vc.ViewController, NSViewControllerTransitionOptions.SlideForward, () => (page as IPageController)?.SendAppearing(), true);
		}

		void UpdateBackgroundColor()
		{
			if (!(View is FormsNSView))
				return;
			var color = Element.BackgroundColor == Color.Default ? Color.White : Element.BackgroundColor;
			(View as FormsNSView).BackgroundColor = color.ToNSColor();
		}

		//TODO: Implement
		void UpdateBarBackgroundColor()
		{

		}

		//TODO: Implement
		void UpdateBarTextColor()
		{

		}

		void HandlePropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (_tracker == null)
				return;

			if (e.PropertyName == NavigationPage.BarBackgroundColorProperty.PropertyName)
				UpdateBarBackgroundColor();
			else if (e.PropertyName == NavigationPage.BarTextColorProperty.PropertyName)
				UpdateBarTextColor();
			else if (e.PropertyName == VisualElement.BackgroundColorProperty.PropertyName)
				UpdateBackgroundColor();
		}
	}
}
