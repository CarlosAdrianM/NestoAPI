using NestoAPI.Models;
using NestoAPI.Models.Sincronizacion;
using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Sincronizacion
{
    /// <summary>
    /// Detecta cambios entre un producto de Nesto y un mensaje externo de sincronización
    /// </summary>
    public class ProductoChangeDetector
    {
        /// <summary>
        /// Detecta qué campos han cambiado entre el producto de Nesto y el mensaje externo
        /// </summary>
        public List<string> DetectarCambios(Producto productoNesto, ExternalSyncMessageDTO productoExterno)
        {
            var cambios = new List<string>();

            if (productoNesto == null)
            {
                cambios.Add("Producto no existe en Nesto");
                return cambios;
            }

            // Comparar Nombre
            if (!string.IsNullOrWhiteSpace(productoExterno.Nombre) &&
                productoExterno.Nombre.Trim() != productoNesto.Nombre?.Trim())
            {
                cambios.Add($"Nombre: '{productoNesto.Nombre?.Trim()}' → '{productoExterno.Nombre.Trim()}'");
            }

            // Comparar PVP (Precio Profesional)
            if (productoExterno.PrecioProfesional.HasValue &&
                productoExterno.PrecioProfesional.Value != productoNesto.PVP)
            {
                cambios.Add($"PVP: {productoNesto.PVP} → {productoExterno.PrecioProfesional.Value}");
            }

            // Comparar Estado
            if (productoExterno.Estado.HasValue &&
                productoExterno.Estado.Value != productoNesto.Estado)
            {
                cambios.Add($"Estado: {productoNesto.Estado} → {productoExterno.Estado.Value}");
            }

            // Comparar RoturaStockProveedor
            if (productoExterno.RoturaStockProveedor.HasValue &&
                productoExterno.RoturaStockProveedor.Value != productoNesto.RoturaStockProveedor)
            {
                cambios.Add($"RoturaStockProveedor: {productoNesto.RoturaStockProveedor} → {productoExterno.RoturaStockProveedor.Value}");
            }

            // Comparar CodigoBarras
            if (!string.IsNullOrWhiteSpace(productoExterno.CodigoBarras) &&
                productoExterno.CodigoBarras.Trim() != productoNesto.CodBarras?.Trim())
            {
                cambios.Add($"CodigoBarras: '{productoNesto.CodBarras?.Trim()}' → '{productoExterno.CodigoBarras.Trim()}'");
            }

            return cambios;
        }
    }
}
