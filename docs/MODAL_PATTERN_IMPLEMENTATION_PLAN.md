# Simplified Blazor WASM Modal Pattern Implementation Plan

This document provides a complete guide for implementing a simple event-driven modal system in a Blazor WebAssembly application. This simplified version removes modal positioning, modal stacking, and modal type enums in favor of a `RenderFragment`-based approach.

---

## Architecture Overview

```
┌─────────────────┐     PostEvent()     ┌──────────────────┐
│ Feature Code    │ ──────────────────> │ Event Service    │
│ (Component/VM)  │                     │ (Singleton)      │
└─────────────────┘                     └────────┬─────────┘
                                                 │
                                    EventReceived event fires
                                                 │
                                                 ▼
                                        ┌──────────────────┐
                                        │ ModalController  │
                                        │ ViewModel        │
                                        │ ────────────     │
                                        │ IsOpen: bool     │
                                        │ Title: string    │
                                        │ Content: RF      │
                                        └────────┬─────────┘
                                                 │
                                    PropertyChanged fires
                                                 │
                                                 ▼
                                        ┌──────────────────┐
                                        │ ModalController  │
                                        │ Component        │
                                        │ ────────────     │
                                        │ @if (IsOpen)     │
                                        │   render modal   │
                                        └──────────────────┘
```

**Key Benefits:**
- Minimal code (~100 lines core logic)
- No type mapping or switch statements
- Content passed as `RenderFragment` - maximum flexibility
- Single modal at a time (simplest mental model)
- Auto-closes on navigation

---

## How the Event System Works

The event system is a simple **publish-subscribe (pub/sub) pattern** that decouples modal triggering from modal rendering.

### Core Concepts

1. **Event Bus**: A singleton service that acts as a message broker
2. **Publishers**: Any component/service that wants to show a modal
3. **Subscribers**: The ModalControllerViewModel listens for modal events

### Event Flow Diagram

```
STEP 1: Feature code wants to show a modal
─────────────────────────────────────────────────────────────────────────

    [DeleteButton.razor]

    @inject IEventNotificationService Events

    void HandleDeleteClick()
    {
        Events.PostEvent(this, new ModalEventArgs
        {
            ModalAction = ModalActions.Open,
            Title = "Confirm Delete",
            Content = @<div>
                <p>Delete this item?</p>
                <button @onclick="ConfirmDelete">Yes</button>
            </div>
        });
    }


STEP 2: EventNotificationService broadcasts to all subscribers
─────────────────────────────────────────────────────────────────────────

    PostEvent() invokes the EventReceived event:

    public void PostEvent(object sender, IEventArgs args)
    {
        EventReceived?.Invoke(sender, args);  // All subscribers notified
    }

    Subscribers (in order of subscription):
    ├── ModalControllerViewModel.OnEventReceived()  ← processes modal events
    ├── AnalyticsService.OnEventReceived()          ← might log events
    └── (any other subscribers)


STEP 3: ModalControllerViewModel processes the event
─────────────────────────────────────────────────────────────────────────

    private Task OnEventReceived(object sender, IEventArgs e)
    {
        // Filter: only care about ModalEventArgs
        if (e is not ModalEventArgs modal) return Task.CompletedTask;

        if (modal.ModalAction == ModalActions.Open)
        {
            Title = modal.Title;
            Content = modal.Content;
            IsOpen = true;  // ← triggers PropertyChanged
        }
        else // Close
        {
            IsOpen = false;
            Content = null;
            Title = null;
        }
        return Task.CompletedTask;
    }


STEP 4: PropertyChanged notifies the UI component
─────────────────────────────────────────────────────────────────────────

    ModalControllerViewModel sets IsOpen = true
        │
        └── OnPropertyChanged("IsOpen") fires
                │
                └── ModalController.razor.cs receives notification:

                    void ViewModel_OnPropertyChanged(...)
                    {
                        InvokeAsync(StateHasChanged);  // Re-render UI
                    }


STEP 5: ModalController component re-renders
─────────────────────────────────────────────────────────────────────────

    @if (ViewModel.IsOpen)
    {
        <div class="modal-overlay">
            <div class="modal-container">
                <h3>@ViewModel.Title</h3>
                @ViewModel.Content        ← RenderFragment renders here
            </div>
        </div>
    }
```

### Why Use Events Instead of Direct Calls?

| Approach | Pros | Cons |
|----------|------|------|
| **Direct injection** (`@inject IModalService`) | Simple, obvious | Tight coupling, harder to test |
| **Events** | Loose coupling, multiple subscribers, easy testing | Slightly more indirection |

Events allow:
- Any component to trigger a modal without knowing about the modal system
- Multiple services to react to modal events (analytics, logging)
- Easy mocking in tests (just don't subscribe)
- Future extension (add toast notifications using same event bus)

### Event Filtering Pattern

The ViewModel filters events by type. This allows the same event bus to carry different event types:

```csharp
private Task OnEventReceived(object sender, IEventArgs e)
{
    // Only process ModalEventArgs - ignore everything else
    if (e is not ModalEventArgs modal) return Task.CompletedTask;

    // Now we know it's a modal event, process it
    // ...
}
```

Other services can use the same event bus for their own event types:

```csharp
// In ToastService
private Task OnEventReceived(object sender, IEventArgs e)
{
    if (e is not ToastEventArgs toast) return Task.CompletedTask;
    // Show toast...
}
```

---

## Implementation Steps

### Step 1: Create the Event Infrastructure

#### 1.1 Event Args Interface

**File:** `Events/IEventArgs.cs`

```csharp
namespace YourApp.Events;

/// <summary>
/// Marker interface for all application events.
/// </summary>
public interface IEventArgs { }
```

#### 1.2 Modal Event Args

**File:** `Events/ModalEventArgs.cs`

```csharp
namespace YourApp.Events;

using Microsoft.AspNetCore.Components;

/// <summary>
/// Event arguments for modal operations.
/// </summary>
public class ModalEventArgs : IEventArgs
{
    public enum ModalActions
    {
        Open,
        Close
    }

    /// <summary>
    /// The action to perform (Open or Close).
    /// </summary>
    public required ModalActions ModalAction { get; init; }

    /// <summary>
    /// The modal title. Required when opening.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// The modal content as a RenderFragment. Required when opening.
    /// </summary>
    public RenderFragment? Content { get; init; }

    /// <summary>
    /// Optional footer content (buttons, etc).
    /// </summary>
    public RenderFragment? Footer { get; init; }

    /// <summary>
    /// Whether clicking the overlay closes the modal. Default true.
    /// </summary>
    public bool CloseOnOverlayClick { get; init; } = true;
}
```

#### 1.3 Event Notification Service Interface

**File:** `Services/IEventNotificationService.cs`

```csharp
namespace YourApp.Services;

using YourApp.Events;

/// <summary>
/// Delegate for event handlers.
/// </summary>
public delegate Task EventReceivedEventHandler(object sender, IEventArgs e);

/// <summary>
/// Simple event bus for application-wide communication.
/// </summary>
public interface IEventNotificationService
{
    /// <summary>
    /// Raised when any event is posted. Subscribe to receive events.
    /// </summary>
    event EventReceivedEventHandler? EventReceived;

    /// <summary>
    /// Broadcasts an event to all subscribers.
    /// </summary>
    void PostEvent(object sender, IEventArgs args);
}
```

#### 1.4 Event Notification Service Implementation

**File:** `Services/EventNotificationService.cs`

```csharp
namespace YourApp.Services;

using YourApp.Events;

/// <summary>
/// Singleton event bus implementation.
/// </summary>
public class EventNotificationService : IEventNotificationService
{
    public event EventReceivedEventHandler? EventReceived;

    public void PostEvent(object sender, IEventArgs args)
    {
        // Invoke all subscribers synchronously
        // Each subscriber filters for events they care about
        EventReceived?.Invoke(sender, args);
    }
}
```

---

### Step 2: Create the Modal Controller ViewModel

#### 2.1 Interface

**File:** `ViewModels/IModalControllerViewModel.cs`

```csharp
namespace YourApp.ViewModels;

using Microsoft.AspNetCore.Components;
using System.ComponentModel;

/// <summary>
/// Manages modal state. Subscribe to PropertyChanged for UI updates.
/// </summary>
public interface IModalControllerViewModel : INotifyPropertyChanged
{
    bool IsOpen { get; }
    string? Title { get; }
    RenderFragment? Content { get; }
    RenderFragment? Footer { get; }
    bool CloseOnOverlayClick { get; }
}
```

#### 2.2 Implementation

**File:** `ViewModels/ModalControllerViewModel.cs`

```csharp
namespace YourApp.ViewModels;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using YourApp.Events;
using YourApp.Services;

/// <summary>
/// Subscribes to modal events and maintains modal state.
/// Registered as singleton to persist across component lifecycles.
/// </summary>
public class ModalControllerViewModel : IModalControllerViewModel, IDisposable
{
    private readonly IEventNotificationService _eventService;
    private readonly NavigationManager _navigation;

    public event PropertyChangedEventHandler? PropertyChanged;

    // Backing fields
    private bool _isOpen;
    private string? _title;
    private RenderFragment? _content;
    private RenderFragment? _footer;
    private bool _closeOnOverlayClick = true;

    // Public properties with change notification
    public bool IsOpen
    {
        get => _isOpen;
        private set => SetField(ref _isOpen, value);
    }

    public string? Title
    {
        get => _title;
        private set => SetField(ref _title, value);
    }

    public RenderFragment? Content
    {
        get => _content;
        private set => SetField(ref _content, value);
    }

    public RenderFragment? Footer
    {
        get => _footer;
        private set => SetField(ref _footer, value);
    }

    public bool CloseOnOverlayClick
    {
        get => _closeOnOverlayClick;
        private set => SetField(ref _closeOnOverlayClick, value);
    }

    public ModalControllerViewModel(
        IEventNotificationService eventService,
        NavigationManager navigation)
    {
        _eventService = eventService;
        _navigation = navigation;

        // Subscribe to events
        _eventService.EventReceived += OnEventReceived;
        _navigation.LocationChanged += OnLocationChanged;
    }

    /// <summary>
    /// Handles all events from the event bus.
    /// Filters for ModalEventArgs and ignores everything else.
    /// </summary>
    private Task OnEventReceived(object sender, IEventArgs e)
    {
        // Type filter: only process modal events
        if (e is not ModalEventArgs modal) return Task.CompletedTask;

        switch (modal.ModalAction)
        {
            case ModalEventArgs.ModalActions.Open:
                Title = modal.Title;
                Content = modal.Content;
                Footer = modal.Footer;
                CloseOnOverlayClick = modal.CloseOnOverlayClick;
                IsOpen = true;
                break;

            case ModalEventArgs.ModalActions.Close:
                Close();
                break;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Auto-close modal when user navigates to a different page.
    /// </summary>
    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        if (IsOpen) Close();
    }

    private void Close()
    {
        IsOpen = false;
        Title = null;
        Content = null;
        Footer = null;
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void Dispose()
    {
        _eventService.EventReceived -= OnEventReceived;
        _navigation.LocationChanged -= OnLocationChanged;
        GC.SuppressFinalize(this);
    }
}
```

---

### Step 3: Create the Modal Controller Component

#### 3.1 Component Code-Behind

**File:** `Components/ModalController.razor.cs`

```csharp
namespace YourApp.Components;

using Microsoft.AspNetCore.Components;
using System.ComponentModel;
using YourApp.Events;
using YourApp.Services;
using YourApp.ViewModels;

/// <summary>
/// Renders the modal when open. Place once in MainLayout.
/// </summary>
public partial class ModalController : ComponentBase, IDisposable
{
    [Inject] private IModalControllerViewModel ViewModel { get; set; } = null!;
    [Inject] private IEventNotificationService EventService { get; set; } = null!;

    protected override void OnInitialized()
    {
        // Subscribe to ViewModel changes to trigger re-render
        ViewModel.PropertyChanged += OnViewModelChanged;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Any property change triggers re-render
        InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Called when user clicks overlay or close button.
    /// Posts a Close event to the event bus.
    /// </summary>
    private void HandleClose()
    {
        EventService.PostEvent(this, new ModalEventArgs
        {
            ModalAction = ModalEventArgs.ModalActions.Close
        });
    }

    private void HandleOverlayClick()
    {
        if (ViewModel.CloseOnOverlayClick)
        {
            HandleClose();
        }
    }

    public void Dispose()
    {
        ViewModel.PropertyChanged -= OnViewModelChanged;
        GC.SuppressFinalize(this);
    }
}
```

#### 3.2 Component Markup

**File:** `Components/ModalController.razor`

```razor
@namespace YourApp.Components

@if (ViewModel.IsOpen)
{
    <div class="modal-overlay" @onclick="HandleOverlayClick">
        <div class="modal-container" @onclick:stopPropagation="true">
            <div class="modal-header">
                <h3>@ViewModel.Title</h3>
                <button class="modal-close-btn" @onclick="HandleClose" aria-label="Close">
                    &times;
                </button>
            </div>
            <div class="modal-content">
                @ViewModel.Content
            </div>
            @if (ViewModel.Footer != null)
            {
                <div class="modal-footer">
                    @ViewModel.Footer
                </div>
            }
        </div>
    </div>
}
```

---

### Step 4: Create Modal CSS

**File:** `wwwroot/css/modals.css`

```css
.modal-overlay {
    position: fixed;
    inset: 0;
    background-color: rgba(0, 0, 0, 0.5);
    display: flex;
    justify-content: center;
    align-items: center;
    z-index: 1000;
}

.modal-container {
    background: #fff;
    border-radius: 8px;
    box-shadow: 0 4px 20px rgba(0, 0, 0, 0.15);
    max-width: 500px;
    width: 90%;
    max-height: 90vh;
    display: flex;
    flex-direction: column;
    animation: modal-appear 0.2s ease;
}

@keyframes modal-appear {
    from { opacity: 0; transform: translateY(-20px); }
    to { opacity: 1; transform: translateY(0); }
}

.modal-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    padding: 16px 20px;
    border-bottom: 1px solid #e0e0e0;
}

.modal-header h3 {
    margin: 0;
    font-size: 1.25rem;
    font-weight: 600;
}

.modal-close-btn {
    background: none;
    border: none;
    font-size: 1.5rem;
    cursor: pointer;
    color: #666;
    padding: 0;
    line-height: 1;
}

.modal-close-btn:hover {
    color: #333;
}

.modal-content {
    padding: 20px;
    overflow-y: auto;
    flex: 1;
}

.modal-footer {
    display: flex;
    justify-content: flex-end;
    gap: 12px;
    padding: 16px 20px;
    border-top: 1px solid #e0e0e0;
    background-color: #f8f9fa;
}
```

---

### Step 5: Register Services

**File:** `Program.cs` (add to existing)

```csharp
using YourApp.Services;
using YourApp.ViewModels;

// Event bus - singleton so all components share the same instance
builder.Services.AddSingleton<IEventNotificationService, EventNotificationService>();

// Modal controller - singleton to maintain state across renders
builder.Services.AddSingleton<IModalControllerViewModel, ModalControllerViewModel>();
```

---

### Step 6: Add to Layout

**File:** `Shared/MainLayout.razor` (modify existing)

```razor
@inherits LayoutComponentBase
@using YourApp.Components

<div class="page">
    @* Your existing layout *@
    @Body
</div>

@* Add modal controller at the end - renders above all content *@
<ModalController />
```

---

### Step 7: Add CSS Reference

**File:** `wwwroot/index.html`

```html
<head>
    <link href="css/modals.css" rel="stylesheet" />
</head>
```

---

## Usage Examples

### Basic Confirmation Modal

```razor
@inject IEventNotificationService Events

<button @onclick="ShowConfirm">Delete Item</button>

@code {
    void ShowConfirm()
    {
        Events.PostEvent(this, new ModalEventArgs
        {
            ModalAction = ModalEventArgs.ModalActions.Open,
            Title = "Confirm Delete",
            Content = @<p>Are you sure you want to delete this item?</p>,
            Footer = @<text>
                <button class="btn-secondary" @onclick="CloseModal">Cancel</button>
                <button class="btn-danger" @onclick="DeleteItem">Delete</button>
            </text>
        });
    }

    void CloseModal()
    {
        Events.PostEvent(this, new ModalEventArgs
        {
            ModalAction = ModalEventArgs.ModalActions.Close
        });
    }

    void DeleteItem()
    {
        // Do delete logic
        CloseModal();
    }
}
```

### Form Modal

```razor
@inject IEventNotificationService Events

<button @onclick="ShowEditForm">Edit User</button>

@code {
    private UserModel _editModel = new();

    void ShowEditForm()
    {
        _editModel = new UserModel { Name = "Current Name" };

        Events.PostEvent(this, new ModalEventArgs
        {
            ModalAction = ModalEventArgs.ModalActions.Open,
            Title = "Edit User",
            CloseOnOverlayClick = false,  // Prevent accidental close
            Content = @<EditForm Model="_editModel">
                <div class="form-group">
                    <label>Name</label>
                    <InputText @bind-Value="_editModel.Name" class="form-control" />
                </div>
            </EditForm>,
            Footer = @<text>
                <button class="btn-secondary" @onclick="CloseModal">Cancel</button>
                <button class="btn-primary" @onclick="SaveUser">Save</button>
            </text>
        });
    }

    void SaveUser()
    {
        // Save logic
        CloseModal();
    }

    void CloseModal() => Events.PostEvent(this, new ModalEventArgs
    {
        ModalAction = ModalEventArgs.ModalActions.Close
    });
}
```

### Alert Modal (Simple)

```razor
@inject IEventNotificationService Events

@code {
    void ShowError(string message)
    {
        Events.PostEvent(this, new ModalEventArgs
        {
            ModalAction = ModalEventArgs.ModalActions.Open,
            Title = "Error",
            Content = @<p style="color: red;">@message</p>,
            Footer = @<button class="btn-primary" @onclick="CloseModal">OK</button>
        });
    }

    void CloseModal() => Events.PostEvent(this, new ModalEventArgs
    {
        ModalAction = ModalEventArgs.ModalActions.Close
    });
}
```

### Opening Modal from a Service/ViewModel

```csharp
public class OrderService
{
    private readonly IEventNotificationService _events;

    public OrderService(IEventNotificationService events)
    {
        _events = events;
    }

    public void ConfirmOrder(Order order, Action onConfirm)
    {
        _events.PostEvent(this, new ModalEventArgs
        {
            ModalAction = ModalEventArgs.ModalActions.Open,
            Title = "Confirm Order",
            Content = builder =>
            {
                builder.OpenElement(0, "p");
                builder.AddContent(1, $"Place order for {order.Total:C}?");
                builder.CloseElement();
            },
            Footer = builder =>
            {
                builder.OpenElement(0, "button");
                builder.AddAttribute(1, "onclick", EventCallback.Factory.Create(this, () =>
                {
                    onConfirm();
                    _events.PostEvent(this, new ModalEventArgs
                    {
                        ModalAction = ModalEventArgs.ModalActions.Close
                    });
                }));
                builder.AddContent(2, "Confirm");
                builder.CloseElement();
            }
        });
    }
}
```

---

## Helper Extension (Optional)

For cleaner syntax, add an extension method:

**File:** `Extensions/ModalExtensions.cs`

```csharp
namespace YourApp.Extensions;

using Microsoft.AspNetCore.Components;
using YourApp.Events;
using YourApp.Services;

public static class ModalExtensions
{
    public static void OpenModal(
        this IEventNotificationService events,
        object sender,
        string title,
        RenderFragment content,
        RenderFragment? footer = null,
        bool closeOnOverlayClick = true)
    {
        events.PostEvent(sender, new ModalEventArgs
        {
            ModalAction = ModalEventArgs.ModalActions.Open,
            Title = title,
            Content = content,
            Footer = footer,
            CloseOnOverlayClick = closeOnOverlayClick
        });
    }

    public static void CloseModal(this IEventNotificationService events, object sender)
    {
        events.PostEvent(sender, new ModalEventArgs
        {
            ModalAction = ModalEventArgs.ModalActions.Close
        });
    }
}
```

Usage:
```csharp
Events.OpenModal(this, "Title", @<p>Content</p>);
Events.CloseModal(this);
```

---

## File Structure

```
YourApp/
├── Events/
│   ├── IEventArgs.cs
│   └── ModalEventArgs.cs
├── Services/
│   ├── IEventNotificationService.cs
│   └── EventNotificationService.cs
├── ViewModels/
│   ├── IModalControllerViewModel.cs
│   └── ModalControllerViewModel.cs
├── Components/
│   ├── ModalController.razor
│   └── ModalController.razor.cs
├── Extensions/
│   └── ModalExtensions.cs (optional)
├── Shared/
│   └── MainLayout.razor (modified)
├── wwwroot/css/
│   └── modals.css
└── Program.cs (modified)
```

**Total: 8 files** (vs ~20 in the complex version)

---

## Testing

```csharp
// Unit test example
[Fact]
public void OpenModal_SetsIsOpenTrue()
{
    var eventService = new EventNotificationService();
    var navManager = new MockNavigationManager();
    var vm = new ModalControllerViewModel(eventService, navManager);

    eventService.PostEvent(this, new ModalEventArgs
    {
        ModalAction = ModalEventArgs.ModalActions.Open,
        Title = "Test"
    });

    Assert.True(vm.IsOpen);
    Assert.Equal("Test", vm.Title);
}

[Fact]
public void CloseModal_SetsIsOpenFalse()
{
    // ... open first, then close
    eventService.PostEvent(this, new ModalEventArgs
    {
        ModalAction = ModalEventArgs.ModalActions.Close
    });

    Assert.False(vm.IsOpen);
}
```

---

## Summary

This simplified pattern:
- Uses **~100 lines of core code** (vs 400+ in complex version)
- Requires **8 files** (vs ~20)
- Supports **any modal content** via RenderFragment
- **No type mapping** - content is passed directly
- **No stacking** - one modal at a time
- **No positioning** - always centered
- Still maintains **loose coupling** via events
- Still **auto-closes on navigation**
