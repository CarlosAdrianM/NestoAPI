using System;
using System.Collections.Generic;

namespace NestoAPI.Models.Proyectos
{
    /// <summary>NestoAPI#901: un proyecto (curso del SEPE, evento, feria, campaña…) con sus totales imputados.</summary>
    public class ProyectoDTO
    {
        public int Id { get; set; }
        public string Empresa { get; set; }
        /// <summary>Único por empresa, ≤ 20, en mayúsculas (IMPE0210-2026-1).</summary>
        public string Codigo { get; set; }
        public string Nombre { get; set; }
        /// <summary>CursoSEPE, Evento, Feria, Campana u Otro.</summary>
        public string Tipo { get; set; }
        public DateTime? FechaDesde { get; set; }
        /// <summary>Null = abierto sin fecha de fin.</summary>
        public DateTime? FechaHasta { get; set; }
        /// <summary>Solo lectura: Abierto o Cerrado (se cambia con Cerrar y Reabrir).</summary>
        public string Estado { get; set; }
        /// <summary>La referencia del curso (IMPE0210), opcional.</summary>
        public string Producto { get; set; }
        /// <summary>Evento de Nesto, opcional (sin relación dura).</summary>
        public int? EventoId { get; set; }
        public decimal? PresupuestoIngresos { get; set; }
        public decimal? PresupuestoGastos { get; set; }
        public string Comentarios { get; set; }
        public string Usuario { get; set; }
        public DateTime? FechaAlta { get; set; }
        public string UsuarioModificacion { get; set; }
        public DateTime? FechaModificacion { get; set; }
        /// <summary>Solo lectura: ingresos imputados (abonos restados).</summary>
        public decimal TotalIngresos { get; set; }
        /// <summary>Solo lectura: gastos imputados (abonos restados).</summary>
        public decimal TotalGastos { get; set; }
        /// <summary>Solo lectura: ingresos − gastos.</summary>
        public decimal Resultado { get; set; }
        public int NumeroImputaciones { get; set; }
    }

    /// <summary>NestoAPI#901: una parte del apunte imputada a un proyecto.</summary>
    public class ImputacionApunteDTO
    {
        public int Id { get; set; }
        public int ProyectoId { get; set; }
        public string CodigoProyecto { get; set; }
        public string NombreProyecto { get; set; }
        public string EstadoProyecto { get; set; }
        /// <summary>Siempre positivo.</summary>
        public decimal Importe { get; set; }
        /// <summary>Del importe del apunte, con 2 decimales.</summary>
        public decimal Porcentaje { get; set; }
        public string Comentario { get; set; }
        public string Usuario { get; set; }
        public DateTime? Fecha { get; set; }
    }

    /// <summary>NestoAPI#901: un apunte de Contabilidad con lo imputado y lo pendiente.</summary>
    public class ApunteProyectoDTO
    {
        public string Empresa { get; set; }
        public int NumeroOrden { get; set; }
        public DateTime Fecha { get; set; }
        public string Cuenta { get; set; }
        public string NombreCuenta { get; set; }
        public string Concepto { get; set; }
        public string Documento { get; set; }
        public int? Asiento { get; set; }
        public string Diario { get; set; }
        public decimal Debe { get; set; }
        public decimal Haber { get; set; }
        /// <summary>|Debe − Haber|: lo que se puede repartir (y lo que se manda como ImporteApunte en el PUT).</summary>
        public decimal ImporteApunte { get; set; }
        /// <summary>«Gasto» (6xx) o «Ingreso» (7xx).</summary>
        public string Naturaleza { get; set; }
        /// <summary>Apunte al lado contrario del natural: lo imputado RESTA (abono de compra, rectificativa de venta).</summary>
        public bool EsAbono { get; set; }
        public string Tercero { get; set; }
        /// <summary>«Proveedor», «Cliente» o null.</summary>
        public string TipoTercero { get; set; }
        public string NombreTercero { get; set; }
        public decimal Imputado { get; set; }
        /// <summary>ImporteApunte − Imputado (negativo si el apunte bajó después de imputarlo).</summary>
        public decimal Pendiente { get; set; }
        public decimal PorcentajeImputado { get; set; }
        public List<ImputacionApunteDTO> Imputaciones { get; set; } = new List<ImputacionApunteDTO>();
    }

    /// <summary>NestoAPI#901: GET api/Proyectos/ApuntesPorImputar.</summary>
    public class ApuntesPorImputarDTO
    {
        public string Empresa { get; set; }
        public DateTime Desde { get; set; }
        public DateTime Hasta { get; set; }
        public int Pagina { get; set; }
        public int Tope { get; set; }
        /// <summary>Hay más apuntes con ese filtro (pedir la página siguiente o afinar el filtro).</summary>
        public bool HayMas { get; set; }
        public List<ApunteProyectoDTO> Apuntes { get; set; } = new List<ApunteProyectoDTO>();
    }

    /// <summary>NestoAPI#901: GET/PUT api/Proyectos/Apuntes/{numeroOrden}/Imputaciones.</summary>
    public class ImputacionesApunteDTO
    {
        public string Empresa { get; set; }
        public int NumeroOrden { get; set; }
        /// <summary>Null si el apunte ya no está en la contabilidad (y quedan imputaciones huérfanas).</summary>
        public ApunteProyectoDTO Apunte { get; set; }
        public List<ImputacionApunteDTO> Imputaciones { get; set; } = new List<ImputacionApunteDTO>();
        /// <summary>Solo en el PUT: se ha escrito algo.</summary>
        public bool Cambiado { get; set; }
        public List<string> Avisos { get; set; } = new List<string>();
    }

    /// <summary>NestoAPI#901: cuerpo del PUT api/Proyectos/Apuntes/{numeroOrden}/Imputaciones (SUSTITUYE el reparto; lista vacía = quitar).</summary>
    public class PeticionImputacionesApunteDTO
    {
        public string Empresa { get; set; }
        /// <summary>Opcional: el importe del apunte que vio quien reparte; si ya no es ese, 409 APUNTE_CAMBIADO.</summary>
        public decimal? ImporteApunte { get; set; }
        public List<ImputacionPedidaDTO> Imputaciones { get; set; } = new List<ImputacionPedidaDTO>();
    }

    public class ImputacionPedidaDTO
    {
        public int ProyectoId { get; set; }
        /// <summary>Positivo: la parte del apunte (el signo lo da el apunte).</summary>
        public decimal Importe { get; set; }
        public string Comentario { get; set; }
    }

    /// <summary>NestoAPI#901: una cuenta en la cuenta de explotación.</summary>
    public class LineaCuentaExplotacionDTO
    {
        public string Cuenta { get; set; }
        public string NombreCuenta { get; set; }
        /// <summary>Con los abonos restados.</summary>
        public decimal Importe { get; set; }
        public int NumeroApuntes { get; set; }
    }

    /// <summary>NestoAPI#901: un apunte imputado en el detalle de la cuenta de explotación.</summary>
    public class DetalleCuentaExplotacionDTO
    {
        public int IdImputacion { get; set; }
        public int NumeroOrdenApunte { get; set; }
        public DateTime Fecha { get; set; }
        public string Cuenta { get; set; }
        public string NombreCuenta { get; set; }
        public string Naturaleza { get; set; }
        public string Documento { get; set; }
        public string Concepto { get; set; }
        public int? Asiento { get; set; }
        public string Tercero { get; set; }
        public string TipoTercero { get; set; }
        public string NombreTercero { get; set; }
        public decimal Debe { get; set; }
        public decimal Haber { get; set; }
        public decimal ImporteApunte { get; set; }
        /// <summary>Positivo, como se guarda.</summary>
        public decimal Importe { get; set; }
        /// <summary>Lo que suma (o resta, si es abono) a los ingresos o a los gastos.</summary>
        public decimal Efecto { get; set; }
        public decimal Porcentaje { get; set; }
        public string Comentario { get; set; }
        public string Usuario { get; set; }
        public DateTime FechaImputacion { get; set; }
    }

    /// <summary>NestoAPI#901: GET api/Proyectos/{id}/CuentaExplotacion.</summary>
    public class CuentaExplotacionProyectoDTO
    {
        public ProyectoDTO Proyecto { get; set; }
        public List<LineaCuentaExplotacionDTO> Ingresos { get; set; } = new List<LineaCuentaExplotacionDTO>();
        public List<LineaCuentaExplotacionDTO> Gastos { get; set; } = new List<LineaCuentaExplotacionDTO>();
        public decimal TotalIngresos { get; set; }
        public decimal TotalGastos { get; set; }
        public decimal Resultado { get; set; }
        /// <summary>Resultado / ingresos en %; null sin ingresos.</summary>
        public decimal? Margen { get; set; }
        /// <summary>Solo si el proyecto tiene presupuesto (de ingresos o de gastos).</summary>
        public PresupuestoFrenteARealDTO Presupuesto { get; set; }
        public List<DetalleCuentaExplotacionDTO> Detalle { get; set; } = new List<DetalleCuentaExplotacionDTO>();
    }

    /// <summary>NestoAPI#901: presupuesto frente a real (informativo).</summary>
    public class PresupuestoFrenteARealDTO
    {
        public decimal? PresupuestoIngresos { get; set; }
        public decimal? PresupuestoGastos { get; set; }
        /// <summary>Con los que falten a 0.</summary>
        public decimal PresupuestoResultado { get; set; }
        /// <summary>Real − presupuesto (null si no hay ese presupuesto).</summary>
        public decimal? DesviacionIngresos { get; set; }
        public decimal? DesviacionGastos { get; set; }
        public decimal DesviacionResultado { get; set; }
    }

    /// <summary>NestoAPI#901: una cuenta en la comparativa; <see cref="Importes"/> va en el orden de <see cref="ComparativaProyectosDTO.Proyectos"/>.</summary>
    public class FilaComparativaProyectosDTO
    {
        public string Cuenta { get; set; }
        public string NombreCuenta { get; set; }
        public List<decimal> Importes { get; set; } = new List<decimal>();
    }

    /// <summary>NestoAPI#901: GET api/Proyectos/Comparativa: las cuentas de explotación lado a lado.</summary>
    public class ComparativaProyectosDTO
    {
        public List<ProyectoDTO> Proyectos { get; set; } = new List<ProyectoDTO>();
        public List<FilaComparativaProyectosDTO> Ingresos { get; set; } = new List<FilaComparativaProyectosDTO>();
        public List<FilaComparativaProyectosDTO> Gastos { get; set; } = new List<FilaComparativaProyectosDTO>();
        public List<decimal> TotalesIngresos { get; set; } = new List<decimal>();
        public List<decimal> TotalesGastos { get; set; } = new List<decimal>();
        public List<decimal> Resultados { get; set; } = new List<decimal>();
        public List<decimal?> Margenes { get; set; } = new List<decimal?>();
    }
}
