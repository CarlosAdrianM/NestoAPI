using System;
using System.Collections.Generic;

namespace NestoAPI.Infraestructure.PreciosMedios
{
    /// <summary>
    /// Acceso a datos de la sombra de precios medios (Issue #547, corte b). SOLO LECTURA sobre Productos,
    /// LinPedidoCmp, ExtractoProducto y LinPedidoVta; lo único que escribe es la tabla de diagnóstico
    /// <c>PreciosMediosSombra</c>.
    /// </summary>
    public interface IRepositorioPreciosMedios
    {
        /// <summary><c>Empresas.[IVA por defecto]</c>: 1 → 3, 4 → 4, 5 → 5. Nulo si no tiene.</summary>
        string EmpresaEspejo(string empresa);

        /// <summary>Productos con alguna línea de compra <c>TipoLínea = 1</c> en E + espejo (y ficha en E).</summary>
        IReadOnlyList<string> ProductosConCompras(string empresa, string empresaEspejo);

        /// <summary>Todo lo que hace falta de un producto (2-4 consultas por índice).</summary>
        DatosProductoPrecioMedio LeerDatos(string empresa, string empresaEspejo, string producto, bool conVentas);

        /// <summary>
        /// Última ejecución del job «Precios Medios» de msdb (el SP de los domingos). Nulo si no hay ninguna.
        /// Puede lanzar si no hay permiso para leer msdb: quien llama lo trata como «no se sabe».
        /// </summary>
        EjecucionSPPreciosMedios UltimaEjecucionSP();

        /// <summary>Si ya se ha creado la tabla <c>PreciosMediosSombra</c> (Scripts/Issue547_PreciosMediosSombra.sql).</summary>
        bool ExisteTablaSombra();

        /// <summary>Borra lo registrado de esa empresa en esa fecha de pasada (para que repetir la pasada sea idempotente).</summary>
        void BorrarPasada(DateTime fechaPasada, string empresa);

        /// <summary>Inserta (o sustituye) la fila de (fecha de pasada, empresa, producto).</summary>
        void GuardarFila(FilaPreciosMediosSombra fila);
    }

    /// <summary>Una ejecución del job «Precios Medios» de msdb.</summary>
    public sealed class EjecucionSPPreciosMedios
    {
        public DateTime Inicio { get; set; }

        /// <summary>Nulo mientras está en ejecución.</summary>
        public DateTime? Fin { get; set; }

        public bool EnEjecucion => !Fin.HasValue;
    }

    /// <summary>Una fila de <c>PreciosMediosSombra</c>.</summary>
    public sealed class FilaPreciosMediosSombra
    {
        /// <summary>Valor de <see cref="Producto"/> en la fila resumen de cada pasada y empresa.</summary>
        public const string PRODUCTO_RESUMEN = "(resumen)";

        public DateTime FechaPasada { get; set; }
        public string Empresa { get; set; }
        public string Producto { get; set; }
        public bool EsResumen { get; set; }
        public string Clasificacion { get; set; }
        public decimal? PrecioMedioBD { get; set; }
        public decimal? PrecioMedioCalculado { get; set; }
        public int LineasComparadas { get; set; }
        public int LineasDistintas { get; set; }
        public int? PrimeraLineaDistinta { get; set; }
        public int LineasPendientes { get; set; }
        public int? VentasComparadas { get; set; }
        public int? VentasDistintas { get; set; }
        public string Avisos { get; set; }
        public string Detalle { get; set; }
    }
}
