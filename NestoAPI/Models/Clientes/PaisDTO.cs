namespace NestoAPI.Models.Clientes
{
    /// <summary>
    /// NestoAPI#355 / Nesto#428: país para el SelectorPais y el alta de cliente.
    /// La tabla Paises (diseño #148) no está en el EDMX: se lee por SQL crudo.
    /// </summary>
    public class PaisDTO
    {
        public string Codigo { get; set; }
        public string Nombre { get; set; }
        public bool UnionEuropea { get; set; }
    }
}
