namespace NestoAPI.Models.Bancos
{
    /// <summary>
    /// Nesto#425: banco ligero para el SelectorBanco de ControlesUsuario (combo con el
    /// nombre en vez del código pelado). No exponer la entidad EF entera.
    /// </summary>
    public class BancoSelectorDTO
    {
        public string Numero { get; set; }
        public string Nombre { get; set; }
        public string Entidad { get; set; }
        public string Sucursal { get; set; }
    }
}
