using System;
using System.Collections.Generic;

namespace NestoAPI.Models.Traspasos
{
    /// <summary>
    /// NestoAPI#553 (fase 1, corte a): propuesta de reposición de un almacén a otro, tal y como la
    /// calcula prdRellenarReposicionStock. Es de SOLO LECTURA: no crea el traspaso ni escribe en
    /// PreExtrProducto (eso llega en el siguiente corte).
    /// </summary>
    public class PropuestaTraspasoDTO
    {
        public string Empresa { get; set; }
        public string Origen { get; set; }
        public string Destino { get; set; }
        public DateTime Fecha { get; set; }
        public List<LineaPropuestaTraspasoDTO> Lineas { get; set; } = new List<LineaPropuestaTraspasoDTO>();
    }

    public class LineaPropuestaTraspasoDTO
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public string Grupo { get; set; }
        public int StockOrigen { get; set; }
        public int StockDestino { get; set; }
        public int CantidadMaximaDestino { get; set; }
        public int CantidadPendienteServirOrigen { get; set; }
        public int CantidadPendienteServirDestino { get; set; }
        public int CantidadReposicion { get; set; }
        public int Multiplos { get; set; }
    }

    /// <summary>
    /// Fila cruda del SELECT final de prdRellenarReposicionStock (NestoAPI#553). Los nombres tienen
    /// que coincidir con las columnas del SP porque Database.SqlQuery mapea por nombre; por eso
    /// «Número» lleva tilde. Los tipos: smallint en las cantidades calculadas y int en las constantes.
    /// Columnas que el SP devuelve y no se usan (cantidadReponer duplicada, PedidoPor, StockIntermedio,
    /// VariasOpciones, CantidadPedidoEspecial) no se declaran: SqlQuery ignora las que sobran.
    /// </summary>
    public class LineaReposicionStockSP
    {
        public string Empresa { get; set; }
        public string Número { get; set; }
        public string Grupo { get; set; }
        public string Texto { get; set; }
        public string Almacen { get; set; }
        public short StockOrigen { get; set; }
        public short StockDestino { get; set; }
        public short CantidadMaximaDestino { get; set; }
        public short CantidadPendienteServirOrigen { get; set; }
        public short CantidadPendienteServirDestino { get; set; }
        public short? CantidadReposicion { get; set; }
        public int Multiplos { get; set; }
    }
}
