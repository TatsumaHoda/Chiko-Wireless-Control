using System.Threading.Tasks;
using Microsoft.Maui.Controls;

namespace Chiko.WirelessControl.App.Views;

/// <summary>
/// iOS / Android / Windows で安定して使うための
/// パスワード入力ヘルパー。
/// 独自モーダルは使わず、DisplayPromptAsync を利用する。
/// </summary>
public static class PasswordPromptPage
{
    public static Task<string?> ShowAsync(
        Page owner,
        string title,
        string message,
        string placeholder = "Password")
    {
        return owner.DisplayPromptAsync(
            title: title,
            message: message,
            accept: "OK",
            cancel: "Cancel",
            placeholder: placeholder,
            maxLength: -1,
            keyboard: Keyboard.Text,
            initialValue: string.Empty);
    }
}