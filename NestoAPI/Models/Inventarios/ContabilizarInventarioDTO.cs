using System;
using System.Collections.Generic;

namespace NestoAPI.Models.Inventarios
{
    /// <summary>
    /// NestoAPI#790: las comprobaciones de antes de contabilizar el inventario de un almacén y un día (GET
    /// api/Inventarios/Comprobaciones, y el cuerpo del 409 de POST api/Inventarios/Contabilizar). Solo van las comprobaciones
    /// que tienen filas: sin bloqueantes se puede contabilizar; los avisos se enseñan pero no lo impiden.
    /// </summary>
    public class ComprobacionesInventarioDTO
    {
        public string Almacen { get; set; }
        public DateTime Fecha { get; set; }
        public bool ControlUbicaciones { get; set; }
        public List<ComprobacionInventarioDTO> Bloqueantes { get; set; } = new List<ComprobacionInventarioDTO>();
        public List<ComprobacionInventarioDTO> Avisos { get; set; } = new List<ComprobacionInventarioDTO>();
        /// <summary>Quién ha metido inventario ese día en ese almacén (filas pendientes de cada uno).</summary>
        public List<RecuentoUsuarioInventarioDTO> Recuento { get; set; } = new List<RecuentoUsuarioInventarioDTO>();
    }

    public class ComprobacionInventarioDTO
    {
        /// <summary>OtraFecha, CantidadDisparatada, Ficticio, UbicacionMalFormada, SinUbicacion, UbicacionInexistente, AnuladoConStock,
        /// SinPrecioMedio, CalculadoNegativo, UbicacionSinRellenar, GruposMezclados; NestoAPI#784: FilasDuplicadas y RecontadaSinContar
        /// (bloqueantes) y ContadoCeroConStock (aviso) (<see cref="Infraestructure.Inventarios.CodigosComprobacionInventario"/>).</summary>
        public string Codigo { get; set; }
        public string Titulo { get; set; }
        /// <summary>
        /// NestoAPI#784: un bloqueante que el usuario puede confirmar (hoy solo RecontadaSinContar): POST Contabilizar con
        /// ConfirmarContadoCero = true lo deja pasar. Los demás bloqueantes no se saltan nunca.
        /// </summary>
        public bool SeConfirma { get; set; }
        public List<FilaComprobacionInventarioDTO> Filas { get; set; } = new List<FilaComprobacionInventarioDTO>();
    }

    public class FilaComprobacionInventarioDTO
    {
        /// <summary>La fila de Inventarios; null en UbicacionSinRellenar (es una fila de Ubicaciones, no del inventario).</summary>
        public int? NºOrden { get; set; }
        /// <summary>Épica #784: el mismo NºOrden, con nombre sin «º», para DELETE api/Inventarios/Filas/{numeroOrden} (null en UbicacionSinRellenar).</summary>
        public int? NumeroOrden => NºOrden;
        /// <summary>
        /// Épica #784 (Carlos 11/11/26): quien pregunta puede borrar esta fila con DELETE api/Inventarios/Filas/{NumeroOrden}: es una
        /// fila pendiente de hoy que sobra (bloqueantes SinUbicacion o FilasDuplicadas) y tiene permiso. Los clientes solo ofrecen
        /// borrar donde venga a true.
        /// </summary>
        public bool SePuedeBorrar { get; set; }
        public string Producto { get; set; }
        public string Descripcion { get; set; }
        public int? StockCalculado { get; set; }
        public int? StockReal { get; set; }
        public string Pasillo { get; set; }
        public string Fila { get; set; }
        public string Columna { get; set; }
        public string Usuario { get; set; }
        public DateTime? Fecha { get; set; }
    }

    public class RecuentoUsuarioInventarioDTO
    {
        public string Usuario { get; set; }
        public int Filas { get; set; }
    }

    /// <summary>NestoAPI#790: el cuerpo de POST api/Inventarios/Contabilizar.</summary>
    public class ContabilizarInventarioParametrosDTO
    {
        public string Empresa { get; set; }
        public string Almacen { get; set; }
        public DateTime Fecha { get; set; }
        /// <summary>
        /// NestoAPI#784: el usuario confirma que las filas de RecontadaSinContar (productos ya contabilizados ese día que se han vuelto
        /// a abrir y siguen con contado 0) de verdad tienen 0. Sin él, ese bloqueante da 409 como los demás.
        /// </summary>
        public bool ConfirmarContadoCero { get; set; }
    }

    /// <summary>NestoAPI#790: lo contabilizado (un traspaso por usuario) y los usuarios en los que el procedimiento ha fallado.</summary>
    public class ContabilizacionInventarioDTO
    {
        public string Almacen { get; set; }
        public DateTime Fecha { get; set; }
        public List<UsuarioContabilizadoInventarioDTO> Contabilizados { get; set; } = new List<UsuarioContabilizadoInventarioDTO>();
        public List<ErrorContabilizarInventarioDTO> Errores { get; set; } = new List<ErrorContabilizarInventarioDTO>();
    }

    public class UsuarioContabilizadoInventarioDTO
    {
        public string Usuario { get; set; }
        public int Filas { get; set; }
        public int? Traspaso { get; set; }
    }

    public class ErrorContabilizarInventarioDTO
    {
        public string Usuario { get; set; }
        public string Mensaje { get; set; }
    }

    /// <summary>NestoAPI#790: GET api/Inventarios/Diferencias. Valoracion «PVP» (las tiendas) o «Coste» (PrecioMedio).</summary>
    public class DiferenciasInventarioDTO
    {
        public string Almacen { get; set; }
        public DateTime? Fecha { get; set; }
        public List<int> Traspasos { get; set; } = new List<int>();
        public string Valoracion { get; set; }
        public List<LineaDiferenciaInventarioDTO> Lineas { get; set; } = new List<LineaDiferenciaInventarioDTO>();
        public TotalesDiferenciasInventarioDTO Totales { get; set; } = new TotalesDiferenciasInventarioDTO();
        public List<TotalGrupoDiferenciasInventarioDTO> PorGrupo { get; set; } = new List<TotalGrupoDiferenciasInventarioDTO>();
        public List<TotalFamiliaDiferenciasInventarioDTO> PorFamilia { get; set; } = new List<TotalFamiliaDiferenciasInventarioDTO>();
    }

    public class LineaDiferenciaInventarioDTO
    {
        public string Producto { get; set; }
        public string Descripcion { get; set; }
        public string Grupo { get; set; }
        public string Familia { get; set; }
        public string Pasillo { get; set; }
        public string Fila { get; set; }
        public string Columna { get; set; }
        public int StockCalculado { get; set; }
        public int StockReal { get; set; }
        /// <summary>StockReal − StockCalculado: positiva, sobra; negativa, falta.</summary>
        public int Diferencia { get; set; }
        public decimal ValorUnitario { get; set; }
        public decimal ValorDiferencia { get; set; }
        public string Usuario { get; set; }
    }

    public class TotalesDiferenciasInventarioDTO
    {
        public int Filas { get; set; }
        public int Unidades { get; set; }
        public decimal Valor { get; set; }
    }

    public class TotalGrupoDiferenciasInventarioDTO
    {
        public string Grupo { get; set; }
        public int Filas { get; set; }
        public int Unidades { get; set; }
        public decimal Valor { get; set; }
    }

    public class TotalFamiliaDiferenciasInventarioDTO
    {
        public string Familia { get; set; }
        public int Filas { get; set; }
        public int Unidades { get; set; }
        public decimal Valor { get; set; }
    }
}
