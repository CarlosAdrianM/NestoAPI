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
                    MotivoEspecifico = true,
                    Motivo = $"El producto {numeroProducto} no puede ir a ese precio porque no se ha comprado el producto MULTIVIT"
                };
            }

            Producto producto = GestorPrecios.servicio.BuscarProducto(numeroProducto);
            if (producto.SubGrupo == Constantes.Productos.SUBGRUPO_MUESTRAS && producto.Grupo == Constantes.Productos.GRUPO_COSMETICA)
            {
                decimal baseImponiblePedido = pedido.Lineas
                    .Where(l => l.GrupoProducto != Constantes.Productos.GRUPO_APARATOS)
                    .Sum(l => l.BaseImponible);
                // Solo cuentan las unidades sueltas: las que cubre una oferta combinada (incluida 1
                // talla del grupo de alternativas) no gastan el 5 % reservado a las muestras sueltas.
                int unidadesCubiertas = ValidadorOfertasCombinadas.UnidadesCubiertas(pedido, numeroProducto, servicio);
                int maximoUnidades = pedido.Lineas.Where(l => l.Producto == numeroProducto).Sum(l => l.Cantidad) - unidadesCubiertas;

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

                return new RespuestaValidacion
                {
                    ValidacionSuperada = false,
                    ProductoId = numeroProducto,
                    MotivoEspecifico = true,
                    Motivo = maximoUnidades > UNIDADES_MAXIMO_MUESTRAS
                        ? $"El producto {numeroProducto} no se puede regalar: se superan las {UNIDADES_MAXIMO_MUESTRAS} unidades de muestra suelta permitidas"
                        : $"El material promocional suelto (el que no cubre ninguna oferta) supera el 5 % del pedido: no se puede regalar el producto {numeroProducto}"
                };
            }

            return respuesta;
        }

        /// <summary>
        /// Importe (PVP × cantidad) de las muestras cosméticas SUELTAS que cuentan para el límite del 5 %.
        /// Se parte del importe de TODAS las muestras cosméticas y se RESTA lo que cubre una oferta:
        /// (1) las OTRAS muestras justificadas completas por otro validador de aceptación (p. ej. el
        /// regalo de una oferta combinada) y (2) la parte del PROPIO producto validado que cubre una
        /// oferta (p. ej. 1 de las 2 camisetas del grupo de alternativas). Lo que queda son las unidades
        /// sueltas, las únicas que deben "gastar" el 5 %.
        /// Caso real (pedido 918775): las muestras NEO-TECH de la oferta combinada inflaban el importe
        /// (111,94 € sobre una base de 686 €, 5 % = 34,30 €) y rechazaban 4 muestras sueltas que por sí
        /// solas sumaban 10,94 € y sí cabían en el 5 %.
        /// </summary>
        private decimal CalcularImporteMuestrasNoJustificadas(PedidoVentaDTO pedido, string numeroProducto, IServicioPrecios servicio)
        {
            decimal importeTotal = GestorPrecios.servicio.CalcularImporteGrupo(
                pedido, Constantes.Productos.GRUPO_COSMETICA, Constantes.Productos.SUBGRUPO_MUESTRAS);

            // Otras muestras justificadas por otro validador de aceptación (p. ej. el regalo de una
            // oferta combinada): se restan completas porque no son muestras sueltas.
            decimal importeJustificadas = pedido.Lineas
                .Where(l => l.Producto != null
                    && l.Producto.Trim() != numeroProducto.Trim()
                    && l.GrupoProducto?.Trim() == Constantes.Productos.GRUPO_COSMETICA
                    && l.SubgrupoProducto?.Trim() == Constantes.Productos.SUBGRUPO_MUESTRAS
                    && OtroValidadorDeAceptacionLaJustifica(pedido, l.Producto.Trim(), servicio))
                .Sum(l => (GestorPrecios.servicio.BuscarProducto(l.Producto).PVP ?? 0) * l.Cantidad);

            // Del propio producto validado puede haber una parte cubierta por una oferta (p. ej. 1 de
            // las 2 camisetas del grupo de alternativas): esas unidades tampoco gastan el 5 %. Solo
            // cuentan las sobrantes. OtroValidador... excluye el propio producto, así que se resta aquí.
            int unidadesCubiertasPropias = ValidadorOfertasCombinadas.UnidadesCubiertas(pedido, numeroProducto, servicio);
            decimal coberturaPropia = (GestorPrecios.servicio.BuscarProducto(numeroProducto).PVP ?? 0) * unidadesCubiertasPropias;

            return importeTotal - importeJustificadas - coberturaPropia;
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