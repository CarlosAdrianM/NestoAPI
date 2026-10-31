using System;
using System.Globalization;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>NestoAPI#770: cómo ha pagado quien firma (para la frase de conformidad).</summary>
    public enum FormaPagoConformidad
    {
        Ninguno,
        Efectivo,
        Tarjeta
    }

    /// <summary>
    /// NestoAPI#770: el texto de conformidad que la PDA enseña encima del recuadro de firma y que el servidor sella en la imagen.
    /// La MISMA regla que Ariadna.Core/Reparto/TextoConformidadEntrega.cs (Ariadna#71): el servidor solo la usa si la PDA no
    /// manda el texto (PDAs viejas). Si se cambia aquí, se cambia allí.
    /// </summary>
    public static class TextoConformidadEntrega
    {
        private static readonly CultureInfo Espanol = new CultureInfo("es-ES");

        /// <summary>«1 bulto», «3 bultos».</summary>
        public static string Bultos(int bultos) => bultos == 1 ? "1 bulto" : $"{bultos} bultos";

        /// <summary>«45,30 €».</summary>
        public static string Importe(decimal importe) => importe.ToString("N2", Espanol) + " €";

        /// <summary>
        /// «Confirmo que recibo 3 bultos del pedido 928000 de Nueva Visión en buen estado aparente y que he pagado 45,30 € en
        /// efectivo al repartidor.» En una nota de entrega, «de la nota NE929667» (Carlos, 31/10/26). Sin pedido, sin «del pedido …»; sin forma de pago o sin importe, sin la parte del pago.
        /// </summary>
        public static string Entrega(int bultos, int? pedido, FormaPagoConformidad forma, decimal? importe, bool esNotaEntrega = false)
        {
            string texto = $"Confirmo que recibo {Bultos(bultos)}" + (pedido > 0 ? (esNotaEntrega ? $" de la nota NE{pedido.Value}" : $" del pedido {pedido.Value}") : string.Empty)
                + " de Nueva Visión en buen estado aparente";
            if (importe > 0)
            {
                switch (forma)
                {
                    case FormaPagoConformidad.Efectivo:
                        texto += $" y que he pagado {Importe(importe.Value)} en efectivo al repartidor";
                        break;
                    case FormaPagoConformidad.Tarjeta:
                        texto += $" y que he pagado {Importe(importe.Value)} con tarjeta";
                        break;
                }
            }
            return texto + ".";
        }

        /// <summary>«Confirmo que entrego 2 bultos al repartidor de Nueva Visión.»</summary>
        public static string Recogida(int bultos) => $"Confirmo que entrego {Bultos(bultos)} al repartidor de Nueva Visión.";

        /// <summary>El CobroReembolso de la API («Efectivo», «Tarjeta»…) como forma de pago de la frase: el resto, Ninguno.</summary>
        public static FormaPagoConformidad FormaDe(string cobro)
        {
            if (string.Equals(cobro, Models.Agencias.PruebaEntregaReparto.COBRO_EFECTIVO, StringComparison.OrdinalIgnoreCase))
            {
                return FormaPagoConformidad.Efectivo;
            }
            if (string.Equals(cobro, Models.Agencias.PruebaEntregaReparto.COBRO_TARJETA, StringComparison.OrdinalIgnoreCase))
            {
                return FormaPagoConformidad.Tarjeta;
            }
            return FormaPagoConformidad.Ninguno;
        }
    }
}
