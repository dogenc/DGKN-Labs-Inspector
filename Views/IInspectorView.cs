namespace DGKNInspector.Views;

/// <summary>Gemeinsame Schnittstelle der Tabs, damit das Hauptfenster
/// Zähler und Fortschritt im Kopf anzeigen kann.</summary>
public interface IInspectorView
{
    event EventHandler? HeaderChanged;

    /// <summary>Anzahl der aktuell (gefiltert) angezeigten Zeilen.</summary>
    int Count { get; }

    /// <summary>Beschriftung unter der Zahl, z. B. „Programme“.</summary>
    string CountLabel { get; }

    /// <summary>Kurzer Fortschritts-/Zeitstempeltext.</summary>
    string Progress { get; }

    /// <summary>Tab wird sichtbar.</summary>
    void Activate();

    /// <summary>Tab wird verlassen (z. B. Auto-Aktualisierung pausieren).</summary>
    void Deactivate();
}
