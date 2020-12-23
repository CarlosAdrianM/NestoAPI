using NestoAPI.Models;
using NestoAPI.Models.SeEstaVendiendo;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;

namespace NestoAPI.Infraestructure.SeEstaVendiendo
{
    public class GestorSeEstaVendiendo
    {
        public static async Task<List<SeEstaVendiendoModel>> ArticulosVendidos(DateTime desde, string usuario)
        {
            List<string> vendidos;
            List<SeEstaVendiendoModel> resultado = new List<SeEstaVendiendoModel>();
            using (NVEntities db = new NVEntities())
            {
                vendidos = db.LinPedidoVtas.Where(l =>
                        l.Fecha_Modificación >= desde && l.Estado >= Constantes.EstadosLineaVenta.EN_CURSO &&
                        l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO && l.Cantidad > 0 && l.Base_Imponible > 0 &&
                        l.Usuario != usuario && l.Grupo != "CUR"
                    ).GroupBy(
                        l => l.Producto,
                        l => l.Cantidad,
                        (producto, suma) => new
                        {
                            Producto = producto,
                            Cuenta = suma.Count()
                        }
                    ).OrderByDescending(c => c.Cuenta).Take(5).Select(c => c.Producto.Trim()).ToList();

                
                foreach (var producto in vendidos)
                {
                    string rutaImagen = await ProductoDTO.RutaImagen(producto).ConfigureAwait(false);
                    string rutaEnlace = await ProductoDTO.RutaEnlace(producto).ConfigureAwait(false);
                    string nombre = db.Productos.Single(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && p.Número == producto).Nombre.Trim();

                    resultado.Add(new SeEstaVendiendoModel
                    {
                        Producto = producto,
                        Nombre = nombre,
                        RutaEnlace = rutaEnlace,
                        RutaImagen = rutaImagen
                    });
                }
            }

            
                
            return resultado;
        }
    }
}