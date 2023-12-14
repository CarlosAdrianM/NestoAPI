using NestoAPI.Models;
using NestoAPI.Models.Kits;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Kits
{
    public class GestorKits
    {
        private readonly IProductoService _servicioProducto;
        private readonly IUbicacionService _servicioUbicacion;

        public GestorKits(IProductoService servicioProducto, IUbicacionService servicioUbicacion)
        {
            _servicioProducto = servicioProducto;
            _servicioUbicacion = servicioUbicacion;
        }
        public async Task<List<PreExtractoProductoDTO>> ProductosMontarKit(string empresa, string almacen, string producto, int cantidad, string usuario)
        {
            ProductoDTO productoMontar = await _servicioProducto.LeerProducto(empresa, producto, true);
            List<PreExtractoProductoDTO> listaPreExtracto = new List<PreExtractoProductoDTO>();
            listaPreExtracto.Add(new PreExtractoProductoDTO
            {
                Empresa = empresa,
                Diario = Constantes.DiariosProducto.MONTAR_KIT,
                Almacen = almacen,
                Producto = producto,
                Cantidad = cantidad,
                Texto = $"{(cantidad > 0 ? "Montaje" : "Desmontaje")} del kit {producto}",
                Grupo = productoMontar.Grupo,
                Usuario = usuario
            });
            foreach (var prod in productoMontar.ProductosKit)
            {
                listaPreExtracto.Add(new PreExtractoProductoDTO
                {
                    Empresa = empresa,
                    Diario = Constantes.DiariosProducto.MONTAR_KIT,
                    Almacen = almacen,
                    Producto = prod.ProductoId,
                    Cantidad = -prod.Cantidad * cantidad,
                    Texto = $"{(cantidad > 0 ? "Montaje" : "Desmontaje")} del kit {producto}",
                    Grupo = productoMontar.Grupo,
                    Usuario = usuario
                });
            }
            return listaPreExtracto;
        }
        public async Task<int> MontarKit(string empresa, string almacen, string producto, int cantidad, string usuario)
        {
            return await MontarKit(await ProductosMontarKit(empresa, almacen, producto, cantidad, usuario));
        }
        public async Task<int> MontarKit(List<PreExtractoProductoDTO> preExtractos)
        {            
            GestorUbicaciones gestorUbicaciones = new GestorUbicaciones(_servicioUbicacion);
            var preExtractosUbicados = await gestorUbicaciones.AsignarUbicacionesMasAntiguas(preExtractos);
            int traspaso = await _servicioUbicacion.PersistirMontarKit(preExtractosUbicados);
            return traspaso;
        }
    }
}