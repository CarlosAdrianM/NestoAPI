using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.Pagos.Aplazame
{
    /// <summary>NestoAPI#663: en qué modo está el pago con Aplázame de la app (parámetro AplazameApp de «(defecto)»).</summary>
    public enum ModoAplazame
    {
        /// <summary>No se ofrece. Es también lo que vale sin fila o con un valor que no se reconoce.</summary>
        Apagado,

        /// <summary>Solo para los clientes de prueba y contra el entorno de pruebas de Aplázame (sin dinero).</summary>
        Sandbox,

        /// <summary>Para todos, con dinero de verdad.</summary>
        Activo
    }

    /// <summary>NestoAPI#663: lo que dice <c>GET api/Pedidos/Cliente/Aplazame/Disponible</c> (contrato con la app).</summary>
    public class DisponibilidadAplazameDTO
    {
        public bool Disponible { get; set; }
        public bool Sandbox { get; set; }
        public string Motivo { get; set; }

        /// <summary>
        /// TNV#91: la clave PÚBLICA de Aplázame, para que la app pinte el widget oficial de cuotas (<c>aplazame.js</c>) en
        /// la ficha de producto y en la forma de pago. Es pública (la tienda la publica en su HTML); la privada nunca sale.
        /// Va siempre que el cliente pueda pagar con Aplázame, aunque ESTE importe no entre en los límites: así la ficha
        /// sabe enseñar las cuotas cuando, al subir la cantidad, el importe entra. Null si el cliente no puede.
        /// </summary>
        public string ClavePublica { get; set; }

        /// <summary>TNV#91: AplazameImporteMinimo (con IVA), con la clave pública. Null: sin límite o sin clave.</summary>
        public decimal? ImporteMinimo { get; set; }

        /// <summary>TNV#91: AplazameImporteMaximo (con IVA), con la clave pública. Null: sin límite o sin clave.</summary>
        public decimal? ImporteMaximo { get; set; }
    }

    /// <summary>
    /// NestoAPI#663: interruptor, límites y claves de Aplázame. Las claves viven en <c>secretos.config</c>
    /// (AplazamePrivateKey / AplazamePublicKey; las mismas para producción y sandbox, que solo cambia la cabecera
    /// Accept). El interruptor y los límites, en ParámetrosUsuario de «(defecto)», para poder encender y apagar sin
    /// publicar.
    /// </summary>
    public class ConfiguracionAplazame
    {
        internal const string CLAVE_PRIVADA = "AplazamePrivateKey";
        internal const string CLAVE_PUBLICA = "AplazamePublicKey";
        internal const string USUARIO_DEFECTO = "(defecto)";
        private static readonly CultureInfo Es = new CultureInfo("es-ES");

        public ModoAplazame Modo { get; set; }
        public ISet<string> ClientesPrueba { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public decimal? ImporteMinimo { get; set; }
        public decimal? ImporteMaximo { get; set; }
        public string ClavePrivada { get; set; }
        public string ClavePublica { get; set; }

        /// <summary>El entorno de Aplázame contra el que van los cobros NUEVOS (los ya creados llevan el suyo en PagoTPV.Sandbox).</summary>
        public bool Sandbox => Modo == ModoAplazame.Sandbox;

        public bool TieneClaves => !string.IsNullOrWhiteSpace(ClavePrivada) && !string.IsNullOrWhiteSpace(ClavePublica);

        public static ConfiguracionAplazame Leer(ILectorParametrosUsuario lector)
        {
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO;
            string Parametro(string clave)
            {
                try
                {
                    return lector?.LeerParametro(empresa, USUARIO_DEFECTO, clave);
                }
                catch (Exception)
                {
                    // Sin poder leer el interruptor, apagado: un fallo de lectura no puede encender el pago
                    return null;
                }
            }

            return new ConfiguracionAplazame
            {
                Modo = InterpretarModo(Parametro(Constantes.ParametrosUsuario.APLAZAME_APP)),
                ClientesPrueba = InterpretarClientes(Parametro(Constantes.ParametrosUsuario.APLAZAME_APP_CLIENTES_PRUEBA)),
                ImporteMinimo = InterpretarImporte(Parametro(Constantes.ParametrosUsuario.APLAZAME_IMPORTE_MINIMO)),
                ImporteMaximo = InterpretarImporte(Parametro(Constantes.ParametrosUsuario.APLAZAME_IMPORTE_MAXIMO)),
                ClavePrivada = ConfigurationManager.AppSettings[CLAVE_PRIVADA]?.Trim(),
                ClavePublica = ConfigurationManager.AppSettings[CLAVE_PUBLICA]?.Trim()
            };
        }

        internal static ModoAplazame InterpretarModo(string valor)
        {
            switch (valor?.Trim().ToLowerInvariant())
            {
                case "sandbox":
                    return ModoAplazame.Sandbox;
                case "activo":
                    return ModoAplazame.Activo;
                default:
                    return ModoAplazame.Apagado;
            }
        }

        internal static ISet<string> InterpretarClientes(string valor)
        {
            return new HashSet<string>(
                (valor ?? string.Empty).Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(c => c.Trim())
                    .Where(c => c.Length > 0),
                StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>«150», «150,50» o «150.50». Ilegible o vacío: sin límite.</summary>
        internal static decimal? InterpretarImporte(string valor)
        {
            if (string.IsNullOrWhiteSpace(valor))
            {
                return null;
            }
            string normalizado = valor.Trim().Replace(',', '.');
            return decimal.TryParse(normalizado, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal importe)
                ? importe
                : (decimal?)null;
        }

        /// <summary>
        /// NestoAPI#663: si a este cliente, con este carrito, se le ofrece Aplázame. Pura para poder probar cada
        /// condición. Los motivos no los ve el cliente tal cual (la app oculta la opción), pero van de usted por si acaso.
        /// </summary>
        public DisponibilidadAplazameDTO Disponibilidad(string cliente, decimal importe, bool cobrarCarritoAntes, bool ocultaPrecios)
        {
            DisponibilidadAplazameDTO No(string motivo) => new DisponibilidadAplazameDTO { Disponible = false, Sandbox = Sandbox, Motivo = motivo };
            // TNV#91: pasadas las condiciones del cliente, la clave pública y los límites van aunque el importe no entre
            DisponibilidadAplazameDTO ConClave(DisponibilidadAplazameDTO d)
            {
                d.ClavePublica = ClavePublica;
                d.ImporteMinimo = ImporteMinimo;
                d.ImporteMaximo = ImporteMaximo;
                return d;
            }

            if (Modo == ModoAplazame.Apagado)
            {
                return No("El pago con Aplázame no está disponible ahora mismo.");
            }
            if (Modo == ModoAplazame.Sandbox && !ClientesPrueba.Contains(cliente?.Trim() ?? string.Empty))
            {
                return No("El pago con Aplázame no está disponible ahora mismo.");
            }
            if (!TieneClaves)
            {
                return No("El pago con Aplázame no está disponible ahora mismo.");
            }
            if (!cobrarCarritoAntes)
            {
                return No("Ahora mismo el pago se hace al confirmar el pedido.");
            }
            if (ocultaPrecios)
            {
                return No("Su pedido se paga con su forma de pago habitual.");
            }
            if (ImporteMinimo.HasValue && importe < ImporteMinimo.Value)
            {
                return ConClave(No($"Aplázame está disponible para pedidos desde {ImporteMinimo.Value.ToString("N2", Es)} €."));
            }
            if (ImporteMaximo.HasValue && importe > ImporteMaximo.Value)
            {
                return ConClave(No($"Aplázame está disponible para pedidos de hasta {ImporteMaximo.Value.ToString("N2", Es)} €."));
            }
            return ConClave(new DisponibilidadAplazameDTO { Disponible = true, Sandbox = Sandbox, Motivo = null });
        }
    }
}
