using NestoAPI.Models.Facturas;

namespace NestoAPI.Infraestructure
{
    public interface IGestorFacturas
    {
        Factura LeerFactura(string empresa, string numeroFactura);
    }
}