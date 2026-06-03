using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Infraestructure.ValidadoresPedido
{
    
    public class ValidadorMuestrasYMaterialPromocional : IValidadorAceptacion
    {
        private const decimal PORCENTAJE_MAXIMO_MUESTRAS = 0.05M;
        private const int UNIDADES_MAXIMO_MUESTRAS = 10;

        public RespuestaValidacion EsPedidoValido(PedidoVentaDTO pedido, string numeroProducto, IServicioPrecios servicio)
        {
            // TODO: REFACTORIZAR PARA QUE SOLO SE EJECUTE UNA VEZ POR PEDIDO
            RespuestaValidacion respuesta = new RespuestaValidacion
            {
                ValidacionSuperada = false,
                ProductoId = numeroProducto,
                Motivo = "El producto " + numeroProducto
                        + " no puede ir a ese precio porque no es material promocional o se supera el importe autorizado"
            };

            // Comprobar que no se regalen muestras de Multivit de Ainhoa si no compra el producto
            if (ContieneMuestrasMultivitAinhoaPeroNoLlevaElProducto(pedido))
            {
                return new RespuestaValidacion
                {
                    ValidacionSuperada = false,
                    ProductoId = numeroProducto,
                    Motivo = $"El producto {numeroProducto} no puede ir a ese precio porque no se ha comprado el producto MULTIVIT"
                };
            }

            Producto producto = GestorPrecios.servicio.BuscarProducto(numeroProducto);
            if (producto.SubGrupo == Constantes.Productos.SUBGRUPO_MUESTRAS && producto.Grupo == Constantes.Productos.GRUPO_COSMETICA)
            {
                decimal baseImponiblePedido = pedido.Lineas
                    .Where(l => l.GrupoProducto != Constantes.Productos.GRUPO_APARATOS)
                    .Sum(l => l.BaseImponible);
                int maximoUnidades = pedido.Lineas.Where(l => l.Producto == numeroProducto).Sum(l => l.Cantidad);

                var importeMuestras = CalcularImporteMuestrasNoJustificadas(pedido, numeroProducto, servicio);

                if (importeMuestras <= baseImponiblePedido * PORCENTAJE_MAXIMO_MUESTRAS && maximoUnidades <= UNIDADES_MAXIMO_MUESTRAS)
                {
                    return new RespuestaValidacion
                    {
                        ValidacionSuperada = true,
                        ProductoId = numeroProducto,
                        Motivo = "El producto " + numeroProducto
                        + " puede ir a ese precio porque es material promocional y no se supera el importe autorizado"
                    };
                }
                
            }

            return respuesta;
        }

        /// <summary>
        /// Importe (PVP × cantidad) de las muestras cosméticas que cuentan para el límite del 5 %.
        /// Se parte del importe de TODAS las muestras cosméticas y se RESTAN las que ya están
        /// justificadas por otro validador de aceptación (p. ej. las que son el regalo de una oferta
        /// combinada): esas muestras no deben "gastar" el 5 % reservado a las muestras sueltas.
        /// Caso real (pedido 918775): las muestras NEO-TECH de la oferta combinada inflaban el importe
        /// (111,94 € sobre una base de 686 €, 5 % = 34,30 €) y rechazaban 4 muestras sueltas que por sí
        /// solas sumaban 10,94 € y sí cabían en el 5 %.
        /// </summary>
        private decimal CalcularImporteMuestrasNoJustificadas(PedidoVentaDTO pedido, string numeroProducto, IServicioPrecios servicio)
        {
            decimal importeTotal = GestorPrecios.servicio.CalcularImporteGrupo(
                pedido, Constantes.Productos.GRUPO_COSMETICA, Constantes.Productos.SUBGRUPO_MUESTRAS);

            decimal importeJustificadas = pedido.Lineas
                .Where(l => l.Producto != null
                    && l.Producto.Trim() != numeroProducto.Trim()
                    && l.GrupoProducto?.Trim() == Constantes.Productos.GRUPO_COSMETICA
                    && l.SubgrupoProducto?.Trim() == Constantes.Productos.SUBGRUPO_MUESTRAS
                    && OtroValidadorDeAceptacionLaJustifica(pedido, l.Producto.Trim(), servicio))
                .Sum(l => (GestorPrecios.servicio.BuscarProducto(l.Producto).PVP ?? 0) * l.Cantidad);

            return importeTotal - importeJustificadas;
        }

        /// <summary>
        /// ¿Hay algún validador de aceptación DISTINTO de éste que justifique el producto a ese precio?
        /// (típicamente una oferta combinada cuyo regalo es la muestra). Se excluye a sí mismo para no
        /// recursar. Si la lista aún no se ha cargado, devuelve false (comportamiento original).
        /// </summary>
        private bool OtroValidadorDeAceptacionLaJustifica(PedidoVentaDTO pedido, string producto, IServicioPrecios servicio)
        {
            if (GestorPrecios.listaValidadoresAceptacion == null)
            {
                return false;
            }

            return GestorPrecios.listaValidadoresAceptacion
                .Where(v => !(v is ValidadorMuestrasYMaterialPromocional))
                .Any(v => v.EsPedidoValido(pedido, producto, servicio).ValidacionSuperada);
        }

        private bool ContieneMuestrasMultivitAinhoaPeroNoLlevaElProducto(PedidoVentaDTO pedido)
        {
            var productosEspecificos = new List<string> { "43826", "43827", "43829", "43830", "43834", "43835", "43836" };
            bool contieneProductoEspecifico = pedido.Lineas.Any(l => productosEspecificos.Contains(l.Producto));

            if (contieneProductoEspecifico)
            {
                bool condicionNoCumplida = !pedido.Lineas.Any(l =>
                    l.texto.StartsWith("MULTIVIT") &&
                    l.SubgrupoProducto != "MMP"
                );

                if (condicionNoCumplida)
                {
                    return true;
                }
            }

            return false;
        }
    }
}