using NestoAPI.Controllers;
using NestoAPI.Models;
using NestoAPI.Models.OfertasCombinadas;
using NestoAPI.Models.OfertasEscalonadas;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.OfertasAutorizadas
{
    /// <summary>
    /// NestoAPI#233: de la entidad al DTO de las tres modalidades de oferta. Antes vivía en cada
    /// controller del mantenimiento; ahora lo comparten con la lectura de ofertas autorizadas que
    /// consume NestoApp, para que las dos pantallas enseñen exactamente lo mismo.
    /// </summary>
    internal static class MapeadorOfertas
    {
        internal static OfertaCombinadaDTO Combinada(OfertaCombinada oferta)
        {
            return new OfertaCombinadaDTO
            {
                Id = oferta.Id,
                Empresa = oferta.Empresa?.Trim(),
                Nombre = oferta.Nombre?.Trim(),
                ImporteMinimo = oferta.ImporteMinimo,
                FechaDesde = oferta.FechaDesde,
                FechaHasta = oferta.FechaHasta,
                RegalarMenorImporte = oferta.RegalarMenorImporte,
                UnidadesRegaladas = oferta.UnidadesRegaladas < 1 ? (short)1 : oferta.UnidadesRegaladas,
                Usuario = oferta.Usuario?.Trim(),
                FechaModificacion = oferta.FechaModificacion,
                Detalles = oferta.OfertasCombinadasDetalles?.Select(d => new OfertaCombinadaDetalleDTO
                {
                    Id = d.Id,
                    Producto = d.Producto?.Trim(),
                    ProductoNombre = d.Producto1?.Nombre?.Trim(),
                    Familia = d.Familia?.Trim(),
                    FiltroProducto = d.FiltroProducto?.Trim(),
                    Grupo = d.Grupo?.Trim(),
                    Subgrupo = d.Subgrupo?.Trim(),
                    Cantidad = d.Cantidad,
                    Precio = d.Precio,
                    GrupoAlternativa = d.GrupoAlternativa,
                    PermitirCantidadMenor = d.PermitirCantidadMenor
                }).ToList() ?? new List<OfertaCombinadaDetalleDTO>()
            };
        }

        internal static OfertaPermitidaFamiliaDTO Familia(OfertaPermitida oferta, Dictionary<string, string> familias)
        {
            string descripcionFamilia = null;
            if (oferta.Familia != null && familias != null && familias.ContainsKey(oferta.Familia))
            {
                descripcionFamilia = familias[oferta.Familia]?.Trim();
            }

            return new OfertaPermitidaFamiliaDTO
            {
                NOrden = oferta.NºOrden,
                Empresa = oferta.Empresa?.Trim(),
                Familia = oferta.Familia?.Trim(),
                FamiliaDescripcion = descripcionFamilia,
                CantidadConPrecio = oferta.CantidadConPrecio,
                CantidadRegalo = oferta.CantidadRegalo,
                FiltroProducto = oferta.FiltroProducto?.Trim(),
                SubGrupo = OfertasPermitidasFamiliaController.NormalizarSubGrupo(oferta.SubGrupo),
                Denegar = oferta.Denegar,
                Usuario = oferta.Usuario?.Trim(),
                FechaModificacion = oferta.FechaModificación
            };
        }

        internal static OfertaEscalonadaDTO Escalonada(OfertaEscalonada oferta)
        {
            return new OfertaEscalonadaDTO
            {
                Id = oferta.Id,
                Empresa = oferta.Empresa?.Trim(),
                Nombre = oferta.Nombre?.Trim(),
                FechaDesde = oferta.FechaDesde,
                FechaHasta = oferta.FechaHasta,
                Usuario = oferta.Usuario?.Trim(),
                FechaModificacion = oferta.FechaModificacion,
                Productos = oferta.OfertasEscalonadasProductos?.Select(p => new OfertaEscalonadaProductoDTO
                {
                    Id = p.Id,
                    Producto = p.Producto?.Trim(),
                    ProductoNombre = p.Producto1?.Nombre?.Trim(),
                    PrecioBase = p.PrecioBase
                }).ToList() ?? new List<OfertaEscalonadaProductoDTO>(),
                Tramos = oferta.OfertasEscalonadasTramos?
                    .OrderBy(t => t.CantidadMinima)
                    .Select(t => new OfertaEscalonadaTramoDTO
                    {
                        Id = t.Id,
                        CantidadMinima = t.CantidadMinima,
                        Descuento = t.Descuento
                    }).ToList() ?? new List<OfertaEscalonadaTramoDTO>()
            };
        }
    }
}
