using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.Ganavisiones
{
    /// <summary>
    /// DTO para transferir datos de Ganavisiones entre capas.
    /// Issue #94: Sistema Ganavisiones - Backend
    /// </summary>
    public class GanavisionDTO
    {
        public int Id { get; set; }
        public string Empresa { get; set; }
        public string ProductoId { get; set; }
        public string ProductoNombre { get; set; }
        public int Ganavisiones { get; set; }
        public DateTime FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }
        public DateTime FechaCreacion { get; set; }
        public DateTime FechaModificacion { get; set; }
        public string Usuario { get; set; }
    }

    /// <summary>
    /// DTO para crear o actualizar un registro de Ganavisiones.
    /// </summary>
    public class GanavisionCreateDTO
    {
        public string Empresa { get; set; }
        public string ProductoId { get; set; }
        public int? Ganavisiones { get; set; }
        public DateTime FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }
    }

    /// <summary>
    /// DTO para un producto que se puede bonificar con Ganavisiones.
    /// Issue #94: Sistema Ganavisiones - FASE 2
    /// </summary>
    public class ProductoBonificableDTO
    {
        public string ProductoId { get; set; }
        public string ProductoNombre { get; set; }
        public int Ganavisiones { get; set; }
        public decimal PVP { get; set; }
        /// <summary>
        /// IVA del producto (IVA_Repercutido). Necesario para crear líneas de pedido correctamente.
        /// Fix: Clientes con recargo de equivalencia (R52) fallaban porque se usaba el IVA del cliente.
        /// </summary>
        public string Iva { get; set; }
        public List<StockAlmacenDTO> Stocks { get; set; }
        public int StockTotal => Stocks?.Sum(s => s.stock) ?? 0;
    }

    /// <summary>
    /// Respuesta del endpoint ProductosBonificables con contexto de Ganavisiones disponibles.
    /// Issue #94: Sistema Ganavisiones - FASE 2
    /// </summary>
    public class ProductosBonificablesResponse
    {
        public int GanavisionesDisponibles { get; set; }
        public decimal BaseImponibleBonificable { get; set; }
        public List<ProductoBonificableDTO> Productos { get; set; }
    }

    /// <summary>
    /// Request para validar si se puede desmarcar ServirJunto.
    /// Issue #94: Sistema Ganavisiones - FASE 3
    /// </summary>
    public class ValidarServirJuntoRequest
    {
        public string Almacen { get; set; }
        public List<string> ProductosBonificados { get; set; }
    }

    /// <summary>
    /// Respuesta de la validacion de ServirJunto.
    /// Issue #94: Sistema Ganavisiones - FASE 3
    /// </summary>
    public class ValidarServirJuntoResponse
    {
        public bool PuedeDesmarcar { get; set; }
        public List<ProductoSinStockDTO> ProductosProblematicos { get; set; }
        public string Mensaje { get; set; }
    }

    /// <summary>
    /// Producto bonificado que no tiene stock en el almacen del pedido.
    /// Issue #94: Sistema Ganavisiones - FASE 3
    /// </summary>
    public class ProductoSinStockDTO
    {
        public string ProductoId { get; set; }
        public string ProductoNombre { get; set; }
        public string AlmacenConStock { get; set; }
    }
}
