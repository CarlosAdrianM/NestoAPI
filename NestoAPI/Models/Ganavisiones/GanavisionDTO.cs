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
        public string Familia { get; set; }
        public int Ganavisiones { get; set; }
        public DateTime FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }
        public DateTime FechaCreacion { get; set; }
        public DateTime FechaModificacion { get; set; }
        public string Usuario { get; set; }
        public int Stock { get; set; }
        public int CantidadRegalada { get; set; }
        public decimal ImporteMinimoPedido { get; set; }
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
        public decimal ImporteMinimoPedido { get; set; }
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
        public int DisponibleTotal => Stocks?.Sum(s => s.cantidadDisponible) ?? 0;
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

    // Los DTOs de validación de "Servir junto" se movieron a
    // NestoAPI.Models.PedidosVenta.ServirJunto (NestoAPI#161) porque conceptualmente
    // pertenecen al pedido, no al módulo de Ganavisiones.
}
