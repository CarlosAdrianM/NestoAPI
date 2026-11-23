using System;
using System.Collections.Generic;

namespace NestoAPI.Models.Informes
{
    /// <summary>
    /// NestoAPI#886: una fila tal como la devuelve la consulta de ExtractoProducto (sin limpiar). Solo lectura. NUNCA lleva coste:
    /// las tiendas no pueden ver el precio de coste (el valor es siempre a PVP).
    /// </summary>
    public class FilaAltaBajaTiendaDTO
    {
        public string Empresa { get; set; }
        public int NumeroOrden { get; set; }
        public string Producto { get; set; }
        public string Almacen { get; set; }
        public DateTime Fecha { get; set; }
        public DateTime FechaModificacion { get; set; }
        public int Cantidad { get; set; }
        public string Diario { get; set; }
        public string Texto { get; set; }
        public string Usuario { get; set; }
        public string Nombre { get; set; }
        public string Familia { get; set; }
        public decimal? Pvp { get; set; }
    }

    /// <summary>NestoAPI#886: un movimiento de alta (+) o baja (−) del stock de la tienda, valorado a PVP.</summary>
    public class MovimientoAltaBajaTiendaDTO
    {
        public DateTime Fecha { get; set; }
        /// <summary>Fecha y hora del apunte ([Fecha Modificación] de ExtractoProducto).</summary>
        public DateTime FechaHora { get; set; }
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public string Familia { get; set; }
        /// <summary>Con signo: + alta, − baja.</summary>
        public int Unidades { get; set; }
        public string Motivo { get; set; }
        /// <summary>Usuario sin dominio.</summary>
        public string Usuario { get; set; }
        public decimal PvpUnitario { get; set; }
        /// <summary>Unidades × PVP (con signo).</summary>
        public decimal ValorPvp { get; set; }
    }

    /// <summary>NestoAPI#886: un producto que en el periodo tiene alguna alta Y alguna baja («aparece y desaparece»).</summary>
    public class ProductoApareceDesapareceDTO
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public string Familia { get; set; }
        public int UnidadesAlta { get; set; }
        /// <summary>En positivo: las unidades dadas de baja.</summary>
        public int UnidadesBaja { get; set; }
        public int UnidadesNeto { get; set; }
        public decimal ValorPvpNeto { get; set; }
    }

    /// <summary>NestoAPI#886: lo dado de alta y de baja en el stock de una tienda en un periodo (GET api/Informes/AltasBajasTienda).</summary>
    public class AltasBajasTiendaDTO
    {
        public string Almacen { get; set; }
        public string NombreTienda { get; set; }
        public DateTime Desde { get; set; }
        public DateTime Hasta { get; set; }
        public List<MovimientoAltaBajaTiendaDTO> Movimientos { get; set; } = new List<MovimientoAltaBajaTiendaDTO>();
        public List<ProductoApareceDesapareceDTO> ApareceYDesaparece { get; set; } = new List<ProductoApareceDesapareceDTO>();
        public int UnidadesAlta { get; set; }
        public decimal ValorPvpAlta { get; set; }
        /// <summary>En positivo: las unidades dadas de baja.</summary>
        public int UnidadesBaja { get; set; }
        /// <summary>En positivo: el valor a PVP de lo dado de baja.</summary>
        public decimal ValorPvpBaja { get; set; }
        public int UnidadesNeto { get; set; }
        public decimal ValorPvpNeto { get; set; }
    }

    /// <summary>NestoAPI#886: lo que contesta POST api/Informes/AltasBajasTienda/Correo (y lo que hace el job con cada tienda).</summary>
    public class CorreoAltasBajasTiendaDTO
    {
        public string Almacen { get; set; }
        public DateTime Desde { get; set; }
        public DateTime Hasta { get; set; }
        public bool Enviado { get; set; }
        public int NumeroMovimientos { get; set; }
        public List<string> Para { get; set; } = new List<string>();
        public List<string> Copia { get; set; } = new List<string>();
        public string Mensaje { get; set; }
    }
}
