using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Pagos.Aplazame
{
    // NestoAPI#663: el cuerpo de POST /checkout (API v4), con los nombres de campo de Aplázame. Los importes, en céntimos;
    // los porcentajes, × 100 (21 % = 2100), que es como serializa los «decimales» el SDK oficial (Decimal::fromFloat).

    public class CheckoutAplazame
    {
        [JsonProperty("toc")] public bool Toc { get; set; } = true;
        [JsonProperty("merchant")] public MerchantAplazame Merchant { get; set; }
        [JsonProperty("order")] public PedidoAplazame Order { get; set; }
        [JsonProperty("customer")] public ClienteAplazame Customer { get; set; }
        [JsonProperty("billing")] public DireccionAplazame Billing { get; set; }
        [JsonProperty("shipping")] public EnvioAplazame Shipping { get; set; }
    }

    public class MerchantAplazame
    {
        [JsonProperty("notification_url")] public string NotificationUrl { get; set; }
        [JsonProperty("success_url")] public string SuccessUrl { get; set; }
        [JsonProperty("pending_url")] public string PendingUrl { get; set; }
        [JsonProperty("error_url")] public string ErrorUrl { get; set; }
        [JsonProperty("ko_url")] public string KoUrl { get; set; }
        [JsonProperty("dismiss_url")] public string DismissUrl { get; set; }
    }

    public class PedidoAplazame
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("currency")] public string Currency { get; set; } = "EUR";
        [JsonProperty("total_amount")] public long TotalAmount { get; set; }
        [JsonProperty("tax_rate")] public long TaxRate { get; set; }
        [JsonProperty("discount")] public long Discount { get; set; }
        [JsonProperty("articles")] public List<ArticuloAplazame> Articles { get; set; } = new List<ArticuloAplazame>();
    }

    public class ArticuloAplazame
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("url")] public string Url { get; set; }
        [JsonProperty("image_url")] public string ImageUrl { get; set; }
        [JsonProperty("quantity")] public int Quantity { get; set; }
        /// <summary>Precio unitario SIN impuestos, en céntimos.</summary>
        [JsonProperty("price")] public long Price { get; set; }
        [JsonProperty("tax_rate")] public long TaxRate { get; set; }
    }

    public class ClienteAplazame
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("email")] public string Email { get; set; }
        /// <summary>«e» = cliente que ya existe.</summary>
        [JsonProperty("type")] public string Type { get; set; } = "e";
        [JsonProperty("language")] public string Language { get; set; } = "es";
    }

    public class DireccionAplazame
    {
        [JsonProperty("first_name")] public string FirstName { get; set; }
        [JsonProperty("last_name")] public string LastName { get; set; }
        [JsonProperty("phone", NullValueHandling = NullValueHandling.Ignore)] public string Phone { get; set; }
        [JsonProperty("street")] public string Street { get; set; }
        [JsonProperty("city")] public string City { get; set; }
        [JsonProperty("state")] public string State { get; set; }
        [JsonProperty("country")] public string Country { get; set; } = "ES";
        [JsonProperty("postcode")] public string Postcode { get; set; }
    }

    public class EnvioAplazame : DireccionAplazame
    {
        [JsonProperty("name")] public string Name { get; set; }
        /// <summary>Portes SIN impuestos, en céntimos.</summary>
        [JsonProperty("price")] public long Price { get; set; }
        [JsonProperty("tax_rate")] public long TaxRate { get; set; }
        [JsonProperty("method", NullValueHandling = NullValueHandling.Ignore)] public string Method { get; set; }
    }

    /// <summary>Una dirección de la ficha (o de la tienda donde se recoge), ya leída.</summary>
    public class DireccionCheckout
    {
        public string Nombre { get; set; }
        public string Direccion { get; set; }
        public string CodigoPostal { get; set; }
        public string Poblacion { get; set; }
        public string Provincia { get; set; }
        public string Pais { get; set; }
        public string Telefono { get; set; }
    }

    /// <summary>
    /// NestoAPI#663: monta el checkout de Aplázame a partir del MISMO pedido que se cobra (el de
    /// CalcularTotalDelCarrito), sin tocar la base de datos, para poder probar el importe al céntimo.
    ///
    /// <para><b>El total es exacto siempre</b>: <c>order.total_amount</c> es el total del pedido en céntimos, sin más, y es
    /// lo que Aplázame financia y lo que nos devuelve en la confirmación para compararlo.</para>
    ///
    /// <para><b>Que los artículos sumen eso</b> (Aplázame pide que artículos + envío − descuento = total, con IVA) no es
    /// gratis: nuestros importes salen de la base de cada LÍNEA (con descuentos y redondeo de Nesto) y Aplázame suma
    /// precio unitario sin IVA × cantidad × (1 + IVA). Estrategia, determinista:</para>
    /// <list type="number">
    /// <item><description>Cada línea de producto va con su cantidad y precio = base de la línea / cantidad si eso da
    /// céntimos exactos; si no, como 1 unidad con el precio de toda la línea y «N x » delante del nombre. Así precio ×
    /// cantidad es SIEMPRE la base de la línea, al céntimo.</description></item>
    /// <item><description>El impuesto de cada artículo es el de su línea (IVA + recargo de equivalencia). Los portes van
    /// en <c>shipping</c> con su base y su IVA. Las líneas negativas (cheque regalo, cupón) no van como artículo: su
    /// importe queda dentro del descuento.</description></item>
    /// <item><description>Se calcula la suma como la haría Aplázame, redondeando cada artículo y el envío al céntimo
    /// (<see cref="ImporteConImpuestos"/>), y la diferencia con el total va a <c>order.discount</c>. Si la suma se quedara
    /// por DEBAJO del total (el descuento no puede ser negativo), se sube un céntimo el precio del artículo más grande
    /// y se repite.</description></item>
    /// </list>
    /// </summary>
    public static class ConstructorCheckoutAplazame
    {
        internal const string URL_IMAGEN_GENERICA = "https://www.productosdeesteticaypeluqueriaprofesional.com/img/cms/Landing/logo.png";

        public static CheckoutAplazame Construir(
            string mid, string correo, string cliente,
            DireccionCheckout facturacion, DireccionCheckout envio, string nombreEnvio, bool recogidaEnTienda,
            IEnumerable<LineaPedidoVentaDTO> lineas, decimal total,
            string urlNotificacion, string esquemaApp)
        {
            PedidoAplazame pedido = ConstruirPedido(mid, lineas, total, out long portes, out long ivaPortes);

            return new CheckoutAplazame
            {
                Merchant = new MerchantAplazame
                {
                    NotificationUrl = urlNotificacion,
                    SuccessUrl = esquemaApp + "ok",
                    PendingUrl = esquemaApp + "pending",
                    ErrorUrl = esquemaApp + "ko",
                    KoUrl = esquemaApp + "ko",
                    DismissUrl = esquemaApp + "dismiss"
                },
                Order = pedido,
                Customer = new ClienteAplazame { Id = cliente?.Trim(), Email = correo?.Trim() },
                Billing = Direccion(new DireccionAplazame(), facturacion),
                Shipping = Envio(envio, nombreEnvio, recogidaEnTienda, portes, ivaPortes)
            };
        }

        /// <summary>El <c>order</c>: artículos, total exacto y el descuento que cuadra la suma. Internal para los tests.</summary>
        internal static PedidoAplazame ConstruirPedido(string mid, IEnumerable<LineaPedidoVentaDTO> lineas, decimal total,
            out long portes, out long ivaPortes)
        {
            List<LineaPedidoVentaDTO> todas = (lineas ?? Enumerable.Empty<LineaPedidoVentaDTO>()).Where(l => l != null).ToList();
            long totalCentimos = Centimos(total);

            List<ArticuloAplazame> articulos = todas
                .Where(l => l.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO && l.BaseImponible >= 0)
                .Select(Articulo)
                .ToList();

            List<LineaPedidoVentaDTO> lineasPortes = todas
                .Where(l => l.tipoLinea != Constantes.TiposLineaVenta.PRODUCTO && l.BaseImponible > 0)
                .ToList();
            portes = lineasPortes.Sum(l => Centimos(l.BaseImponible));
            ivaPortes = lineasPortes.Any() ? PuntosBasicos(Impuestos(lineasPortes.First())) : 0;

            long suma = Sumar(articulos, portes, ivaPortes);
            // El descuento no puede ser negativo: si los redondeos dejan la suma corta, se sube un céntimo el artículo
            // más grande (o los portes, si no hay artículos) hasta pasar del total. Converge en una o dos vueltas.
            int vueltas = 0;
            while (suma < totalCentimos && vueltas++ < 1000)
            {
                ArticuloAplazame mayor = articulos.OrderByDescending(a => a.Price * a.Quantity).ThenBy(a => a.Quantity).FirstOrDefault();
                if (mayor != null)
                {
                    mayor.Price++;
                }
                else
                {
                    portes++;
                }
                suma = Sumar(articulos, portes, ivaPortes);
            }

            return new PedidoAplazame
            {
                Id = mid,
                TotalAmount = totalCentimos,
                TaxRate = articulos.Any() ? articulos.OrderByDescending(a => a.Price * a.Quantity).First().TaxRate : ivaPortes,
                Discount = suma - totalCentimos,
                Articles = articulos
            };
        }

        /// <summary>Lo que suman artículos y envío según Aplázame (cada uno redondeado al céntimo). Internal para los tests.</summary>
        internal static long Sumar(IEnumerable<ArticuloAplazame> articulos, long portes, long ivaPortes)
        {
            return articulos.Sum(a => ImporteConImpuestos(a.Price * a.Quantity, a.TaxRate))
                + ImporteConImpuestos(portes, ivaPortes);
        }

        /// <summary>Base en céntimos × (1 + impuesto), redondeado al céntimo (mitades hacia arriba, como el comercio).</summary>
        internal static long ImporteConImpuestos(long baseCentimos, long puntosBasicos)
        {
            decimal conImpuestos = baseCentimos * (10000M + puntosBasicos) / 10000M;
            return (long)RoundingHelper.Round(conImpuestos, 0);
        }

        private static ArticuloAplazame Articulo(LineaPedidoVentaDTO linea)
        {
            long baseCentimos = Centimos(linea.BaseImponible);
            int cantidad = linea.Cantidad;
            string nombre = string.IsNullOrWhiteSpace(linea.texto) ? linea.Producto?.Trim() : linea.texto.Trim();
            bool exacto = cantidad > 0 && baseCentimos % cantidad == 0;
            return new ArticuloAplazame
            {
                Id = linea.Producto?.Trim(),
                Name = exacto || cantidad <= 1 ? nombre : $"{cantidad} x {nombre}",
                Url = EnlacesTienda.Producto(nombre),
                ImageUrl = URL_IMAGEN_GENERICA,
                Quantity = exacto ? cantidad : 1,
                Price = exacto ? baseCentimos / cantidad : baseCentimos,
                TaxRate = PuntosBasicos(Impuestos(linea))
            };
        }

        private static decimal Impuestos(LineaPedidoVentaDTO linea) => linea.PorcentajeIva + linea.PorcentajeRecargoEquivalencia;

        /// <summary>0,21 → 2100; 0,262 (IVA + recargo) → 2620.</summary>
        internal static long PuntosBasicos(decimal fraccion) => (long)RoundingHelper.Round(fraccion * 10000M, 0);

        internal static long Centimos(decimal importe) => (long)RoundingHelper.Round(importe * 100M, 0);

        private static EnvioAplazame Envio(DireccionCheckout envio, string nombreEnvio, bool recogidaEnTienda, long portes, long ivaPortes)
        {
            EnvioAplazame resultado = Direccion(new EnvioAplazame(), envio);
            resultado.Name = string.IsNullOrWhiteSpace(nombreEnvio) ? "Envío" : nombreEnvio.Trim();
            resultado.Price = portes;
            resultado.TaxRate = ivaPortes;
            resultado.Method = recogidaEnTienda ? "pickup_store" : "postal";
            return resultado;
        }

        private static T Direccion<T>(T destino, DireccionCheckout origen) where T : DireccionAplazame
        {
            string nombre = Limpio(origen?.Nombre) ?? "Cliente";
            int espacio = nombre.IndexOf(' ');
            destino.FirstName = espacio > 0 ? nombre.Substring(0, espacio) : nombre;
            destino.LastName = espacio > 0 ? nombre.Substring(espacio + 1).Trim() : nombre;
            destino.Street = Limpio(origen?.Direccion) ?? "-";
            destino.City = Limpio(origen?.Poblacion) ?? "-";
            destino.State = Limpio(origen?.Provincia) ?? destino.City;
            destino.Postcode = Limpio(origen?.CodigoPostal) ?? "-";
            destino.Country = Pais(origen?.Pais);
            destino.Phone = Limpio(origen?.Telefono);
            return destino;
        }

        /// <summary>Aplázame quiere el código ISO de dos letras; lo que no lo sea, España.</summary>
        internal static string Pais(string pais)
        {
            string limpio = Limpio(pais);
            return limpio != null && limpio.Length == 2 && limpio.All(char.IsLetter) ? limpio.ToUpperInvariant() : "ES";
        }

        private static string Limpio(string texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
    }

    /// <summary>El enlace del producto: la búsqueda de la tienda online, que no cuesta una llamada por producto.</summary>
    internal static class EnlacesTienda
    {
        internal static string Producto(string nombre)
        {
            return CorreosPostCompra.ServicioRecomendacionesPostCompra.GenerarUrlBusquedaTienda(nombre)
                ?? "https://www.productosdeesteticaypeluqueriaprofesional.com";
        }
    }
}
