using System;
using System.Collections.Generic;

namespace NestoAPI.Models.Inventarios
{
    /// <summary>NestoAPI#795: una ubicación que toca contar (prdUbicacionesParaInventario), con lo que ayuda a decidir en la PDA.</summary>
    public class UbicacionParaInventarioDTO
    {
        public string Pasillo { get; set; }
        public string Fila { get; set; }
        public string Columna { get; set; }
        /// <summary>Días desde el último inventario contabilizado de la ubicación (lo calcula el SP).</summary>
        public int DiasUltInventario { get; set; }
        /// <summary>Productos distintos en Ubicaciones (estado ≥ 0) en esa ubicación del almacén.</summary>
        public int Productos { get; set; }
        /// <summary>La ubicación ya tiene filas pendientes (Estado 1, sin traspaso) del día.</summary>
        public bool YaRellenadaHoy { get; set; }
    }

    /// <summary>NestoAPI#795: rellenar una ubicación de un almacén con control de ubicaciones (Algete) con su teórico.</summary>
    public class RellenarUbicacionInventarioDTO
    {
        public string Empresa { get; set; }
        public string Almacen { get; set; }
        /// <summary>El día del inventario (se ignora la hora). Sin fecha, hoy.</summary>
        public DateTime? Fecha { get; set; }
        public string Pasillo { get; set; }
        public string Fila { get; set; }
        public string Columna { get; set; }
    }

    /// <summary>NestoAPI#795: el evolutivo de una tienda: solo una familia o solo una referencia.</summary>
    public class RellenarParcialInventarioDTO
    {
        public string Empresa { get; set; }
        public string Almacen { get; set; }
        /// <summary>El día del inventario (se ignora la hora). Sin fecha, hoy.</summary>
        public DateTime? Fecha { get; set; }
        /// <summary>O la familia...</summary>
        public string Familia { get; set; }
        /// <summary>... o la referencia (o un código de barras: se lee como en la recepción). Solo uno de los dos.</summary>
        public string Producto { get; set; }
    }

    /// <summary>NestoAPI#795: lo que se ha rellenado (ubicación o parcial), con las filas tal como quedan en Inventarios.</summary>
    public class RellenarInventarioFilasRespuestaDTO
    {
        public string Almacen { get; set; }
        public DateTime Fecha { get; set; }
        public string Pasillo { get; set; }
        public string Fila { get; set; }
        public string Columna { get; set; }
        public string Familia { get; set; }
        public string Producto { get; set; }
        public int Filas { get; set; }
        /// <summary>Suma de StockCalculado de las filas.</summary>
        public int Unidades { get; set; }
        public string Usuario { get; set; }
        public List<LineaInventarioContadaDTO> Lineas { get; set; } = new List<LineaInventarioContadaDTO>();
    }

    /// <summary>NestoAPI#795: una fila pendiente (Estado 1, sin traspaso) del inventario.</summary>
    public class LineaInventarioContadaDTO
    {
        /// <summary>Inventarios.NºOrden: el id del PUT api/Inventarios/{id}.</summary>
        public int NumeroOrden { get; set; }
        public string Producto { get; set; }
        public string Descripcion { get; set; }
        public string Familia { get; set; }
        public string Pasillo { get; set; }
        public string Fila { get; set; }
        public string Columna { get; set; }
        public int StockCalculado { get; set; }
        public int StockReal { get; set; }
        public string Usuario { get; set; }
        public DateTime Fecha { get; set; }
        public DateTime FechaModificacion { get; set; }
        public bool? PorCodigoBarras { get; set; }
    }

    /// <summary>NestoAPI#795: lo contado de una ubicación (en las tiendas, una sola «ubicación» sin pasillo).</summary>
    public class ResumenUbicacionContadaDTO
    {
        public string Pasillo { get; set; }
        public string Fila { get; set; }
        public string Columna { get; set; }
        public int Filas { get; set; }
        /// <summary>Suma de StockReal (lo contado).</summary>
        public int Unidades { get; set; }
        /// <summary>Suma de StockCalculado (el teórico).</summary>
        public int UnidadesCalculadas { get; set; }
        public List<string> Usuarios { get; set; } = new List<string>();
        public DateTime UltimaModificacion { get; set; }
    }

    /// <summary>
    /// NestoAPI#795: GET api/Inventarios/Contado. Con filtro (ubicación o producto) vienen las Lineas; sin filtro, el resumen por
    /// ubicación en Ubicaciones (y Lineas vacía).
    /// </summary>
    public class InventarioContadoDTO
    {
        public string Almacen { get; set; }
        public DateTime Fecha { get; set; }
        public List<LineaInventarioContadaDTO> Lineas { get; set; } = new List<LineaInventarioContadaDTO>();
        public List<ResumenUbicacionContadaDTO> Ubicaciones { get; set; } = new List<ResumenUbicacionContadaDTO>();
    }

    /// <summary>NestoAPI#795: el cuerpo del 409 de RellenarUbicacion y RellenarParcial: lo que ya estaba pendiente.</summary>
    public class YaRellenadoInventarioDTO
    {
        public string Message { get; set; }
        public List<LineaInventarioContadaDTO> Lineas { get; set; } = new List<LineaInventarioContadaDTO>();
    }
}
