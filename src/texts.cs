namespace pip_cam
{
    // The mod's own texts in English and Portuguese, picked by the language chosen in the game's settings. Portugal's
    // Portuguese gets the Brazilian texts.
    internal static class texts
    {
        public static bool portuguese
        {
            get
            {
                Settings settings = Settings.Instance;
                string locale = settings != null ? settings.localeIsoCode : null;
                return locale != null && locale.StartsWith("pt", System.StringComparison.OrdinalIgnoreCase);
            }
        }

        public static string pick(string english, string portuguese_text)
        {
            return portuguese ? portuguese_text : english;
        }

        public static string mod_description => pick(
            "Opens small live views of your park in windows, fixed on a spot or following a guest or a ride.",
            "Abre pequenas vistas ao vivo do parque em janelas, paradas num ponto ou seguindo um visitante ou uma atração.");

        public static string window_title => "PiP Cam";

        public static string open_key_name => pick("Open a view", "Abrir uma vista");
        public static string open_key_description => pick(
            "Opens a window with a live view. With the window of a guest, an employee, a ride or a shop in front, it follows them; otherwise it shows the spot in the middle of the screen.",
            "Abre uma janela com uma vista ao vivo. Com a janela de um visitante, funcionário, atração ou loja na frente, segue ele; senão, mostra o ponto do meio da tela.");

        public static string too_many_windows => pick(
            "Close a view first. The most open at once can be changed in the mod's settings.",
            "Feche uma vista antes. O máximo de vistas abertas pode ser mudado nos ajustes do mod.");

        public static string resolution_label => pick("Picture resolution", "Resolução da imagem");
        public static string frames_per_second_label => pick("Frames per second", "Quadros por segundo");
        public static string max_windows_label => pick("Most views open at once", "Máximo de vistas abertas");

        public static string controls_hint => pick(
            "F4 opens a view, and can be changed in the game's controls settings, under PiP Cam. In a view: click to move the main camera there, drag with the middle mouse button to move the view, drag with the right mouse button to turn it, and use the mouse wheel to zoom. Click the title of a view that follows someone or a ride to open their window. Each open view draws the park again, so a lower resolution or fewer frames per second cost less.",
            "F4 abre uma vista e pode ser trocado nos controles do jogo, em PiP Cam. Numa vista: clique para levar a câmera principal até lá, arraste com o botão do meio para mover a vista, arraste com o botão direito para girar e use a rodinha para dar zoom. Clique no título de uma vista que segue alguém ou uma atração para abrir a janela dele. Cada vista aberta desenha o parque de novo, então menos resolução ou menos quadros por segundo custam menos.");
    }
}
