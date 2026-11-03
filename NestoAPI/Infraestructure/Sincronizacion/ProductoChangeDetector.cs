using NestoAPI.Infraestructure.Productos;
using NestoAPI.Models;
using NestoAPI.Models.Sincronizacion;
using System;
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
        public List<string> DetectarCambios(Producto productoNesto, ProductoSyncMessage productoExterno)
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

            // Comparar CodigoBarras. NestoAPI#782: un código que no se va a grabar (no es un GTIN válido) no cuenta como
            // cambio; si contara, cada mensaje con un «1» parecería un cambio y se reenviaría sin fin.
            string codigoBarrasExterno = CodigoBarrasAGrabar(productoExterno.CodigoBarras, productoNesto.Número);
            if (codigoBarrasExterno != null && codigoBarrasExterno != productoNesto.CodBarras?.Trim())
            {
                cambios.Add($"CodigoBarras: '{productoNesto.CodBarras?.Trim()}' → '{codigoBarrasExterno}'");
            }

            return cambios;
        }

        /// <summary>
        /// NestoAPI#782: el código de barras que llega de un sistema externo, recortado, solo si se puede grabar en la ficha;
        /// null si no viene o si no vale. La regla es la de <see cref="ValidadorGtin"/> (la misma del escaneo, del informe
        /// semanal y de la limpieza de #776); además, la propia referencia del producto no es su código de barras.
        /// </summary>
        public static string CodigoBarrasAGrabar(string codigoExterno, string referencia)
        {
            string codigo = ValidadorGtin.SoloSiValido(codigoExterno);
            if (codigo != null && string.Equals(codigo, referencia?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            return codigo;
        }

        /// <summary>NestoAPI#782: por qué no se graba un código externo que viene informado (null si se graba o si no viene).</summary>
        public static string MotivoRechazoCodigoBarras(string codigoExterno, string referencia)
        {
            if (string.IsNullOrWhiteSpace(codigoExterno))
            {
                return null;
            }
            string motivo = ValidadorGtin.MotivoNoValido(codigoExterno);
            if (motivo != null)
            {
                return motivo;
            }
            return CodigoBarrasAGrabar(codigoExterno, referencia) == null ? MOTIVO_ES_LA_REFERENCIA : null;
        }

        public const string MOTIVO_ES_LA_REFERENCIA = "Es la referencia del producto, no su código de barras";
    }
}
