using NestoAPI.Infraestructure.Buscador;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using static NestoAPI.Infraestructure.Buscador.LuceneBuscador;

namespace NestoAPI.Infraestructure
{
    public class ServicioPlantillaVenta : IServicioPlantillaVenta
    {
        public async Task<List<LineaPlantillaVenta>> BusquedaContextual(string filtroProducto, bool usarBusquedaConAND = false)
        {
            List<ProductoResultadoBusqueda> resultadosLucene = LuceneBuscador.BuscarProductos(filtroProducto, usarBusquedaConAND);

            if (!resultadosLucene.Any())
            {
                return new List<LineaPlantillaVenta>();
            }

            var ids = resultadosLucene.Select(r => r.Id).ToList();

            using (var db = new NVEntities())
            {
                var productosQuery =
                    db.Productos
                      .Where(p => ids.Contains(p.Número)
                               && p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO
                               && p.Estado >= 0
                               && !p.Ficticio
                               && p.Grupo != Constantes.Productos.GRUPO_MATERIAS_PRIMAS)
                      .Join(db.SubGruposProductoes, p => new { p.Empresa, grupo = p.Grupo, numero = p.SubGrupo }, s => new { s.Empresa, grupo = s.Grupo, numero = s.Número },
                          (p, s) => new { p, s })
                      .Join(db.ProveedoresProductoes, ps => new { empresa = ps.p.Empresa, producto = ps.p.Número }, r => new { empresa = r.Empresa, producto = r.Nº_Producto },
                          (ps, r) => new { ps.p, ps.s, r })
                      .GroupBy(x => new
                      {
                          x.p.Número,
                          x.p.Nombre,
                          x.p.Tamaño,
                          x.p.UnidadMedida,
                          nombreFamilia = x.p.Familia1 != null ? x.p.Familia1.Descripción : "",
                          estadoFamilia = x.p.Familia1 != null ? x.p.Familia1.Estado : 0,
                          x.p.Estado,
                          nombreSubGrupo = x.s.Descripción,
                          x.p.Aplicar_Dto,
                          x.p.PVP,
                          x.p.IVA_Repercutido,
                          clasificacion = x.p.ClasificacionMasVendido,
                          codigoBarras = x.p.CodBarras
                      })
                      .Select(x => new LineaPlantillaVenta
                      {
                          producto = x.Key.Número.Trim(),
                          texto = x.Key.Nombre.Trim(),
                          tamanno = x.Key.Tamaño,
                          unidadMedida = x.Key.UnidadMedida,
                          familia = x.Key.nombreFamilia.Trim(),
                          estado = x.Key.Estado,
                          subGrupo = x.Key.nombreSubGrupo.Trim(),
                          codigoBarras = x.Key.codigoBarras != null ? x.Key.codigoBarras.Trim() : "",
                          cantidadVendida = 0,
                          cantidadAbonada = 0,
                          fechaUltimaVenta = DateTime.MinValue,
                          aplicarDescuento = x.Key.Aplicar_Dto,
                          iva = x.Key.IVA_Repercutido,
                          precio = x.Key.PVP ?? 0,
                          clasificacionMasVendidos = x.Key.clasificacion != null ? x.Key.clasificacion.Posicion : 0
                      });

                var resultados = await productosQuery.ToListAsync();

                // 👉 Reordenar según el orden original de Lucene
                var diccionarioOrden = ids
                    .Select((id, index) => new { id, index })
                    .ToDictionary(x => x.id.Trim(), x => x.index);

                var ordenados = resultados
                    .OrderBy(r => diccionarioOrden.ContainsKey(r.producto) ? diccionarioOrden[r.producto] : int.MaxValue)
                    .ToList();

                return ordenados;
            }
        }


        public HashSet<string> CargarProductosBonificables()
        {
            return new HashSet<string>
            {
                "15477",
                "27240",
                "25539",
                "18003",
                "18004",
                "22161",
                "25401",
                "33787",
                "27067",
                "21722",
                "32716",
                "24883",
                "20992",
                "35975",
                "39196",
                "39197",
                "20892",
                "39200",
                "38648",
                "24392",
                "23789",
                "22045",
                "22530",
                "22696",
                "16627",
                "32988",
                "26999",
                "12537",
                "20706",
                "20705",
                "24807",
                "26692",
                "20459",
                "21492",
                "39858",
                "25720",
                "25279",
                "25966",
                "24885",
                "24470",
                "39604",
                "34081",
                "34080",
                "29803",
                "29804",
                "24955"
            };
        }

        public HashSet<string> CargarProductosYaComprados(string cliente)
        {
            using (NVEntities db = new NVEntities())
            {
                try
                {
                    DateTime fechaDesde = DateTime.Today.AddYears(-1);
                    HashSet<string> productos = db.LinPedidoVtas.
                    Where(x => x.Nº_Cliente == cliente && x.Base_Imponible > 0 && x.Fecha_Factura > fechaDesde && x.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO).
                    Select(x => x.Producto).ToHashSet();
                    return productos;
                }
                catch (Exception e)
                {
                    throw new Exception("No se han pedido leer los productos ya comprados", e);
                }
            }
        }
    }
}