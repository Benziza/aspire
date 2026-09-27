// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using Aspire.Dashboard.Model;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.FluentUI.AspNetCore.Components;

namespace Aspire.Dashboard.Components;

public partial class AspireMenuItem
{
    [Parameter, EditorRequired]
    public required MenuButtonItem Item { get; set; }

    [Parameter, EditorRequired]
    public required EventCallback<MenuButtonItem> OnItemActivated { get; set; }

    [Parameter, EditorRequired]
    public required EventCallback<MenuButtonItem> OnSecondaryActionClicked { get; set; }

    private Dictionary<string, object> AdditionalMenuItemAttributes =>
        new(Item.AdditionalAttributes ?? ImmutableDictionary<string, object>.Empty)
        {
            { "title", !string.IsNullOrEmpty(Item.Tooltip) ? Item.Tooltip : Item.Text ?? string.Empty }
        };

    private RenderFragment? NestedMenuItems => Item.NestedMenuItems is { Count: > 0 } nestedItems
        ? builder => RenderNestedMenuItems(builder, nestedItems)
        : null;

    private Task HandleItemClicked()
    {
        return Item.Role is MenuItemRole.Checkbox or MenuItemRole.Radio
            ? Task.CompletedTask
            : OnItemActivated.InvokeAsync(Item);
    }

    private Task HandleItemCheckedChanged(bool? isChecked)
    {
        return isChecked is true && Item.Role is MenuItemRole.Checkbox or MenuItemRole.Radio
            ? OnItemActivated.InvokeAsync(Item)
            : Task.CompletedTask;
    }

    private Task HandleSecondaryActionClicked()
    {
        return OnSecondaryActionClicked.InvokeAsync(Item);
    }

    private void RenderNestedMenuItems(RenderTreeBuilder builder, IReadOnlyList<MenuButtonItem> nestedItems)
    {
        foreach (var nestedItem in nestedItems)
        {
            builder.OpenComponent<AspireMenuItem>(0);
            builder.SetKey(nestedItem.RenderKey);
            builder.AddAttribute(1, nameof(Item), nestedItem);
            builder.AddAttribute(2, nameof(OnItemActivated), OnItemActivated);
            builder.AddAttribute(3, nameof(OnSecondaryActionClicked), OnSecondaryActionClicked);
            builder.CloseComponent();
        }
    }

    private static string GetIconSlot(MenuItemRole? role) => role switch
    {
        MenuItemRole.Checkbox or MenuItemRole.Radio => "indicator",
        _ => "start"
    };
}
