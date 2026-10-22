using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#693: el interruptor <c>Pedidos:AvisoPromesaRiesgo</c> del Web.config. Sin clave o con cualquier otro valor,
    /// Apagado (no se recalcula nada). Sombra: se recalcula y se registra en PedidosPromesaRiesgo, pero no se avisa a nadie
    /// (el correo resumen va solo a Informática, para medir). Activo: campana de Nesto y correo resumen.
    /// </summary>
    public enum ModoAvisoPromesaRiesgo
    {
        Apagado,
        Sombra,
        Activo
    }

    public static class InterruptorAvisoPromesaRiesgo
    {
        public const string CLAVE = "Pedidos:AvisoPromesaRiesgo";
        /// <summary>Usuarios de Dirección (sin dominio, separados por comas) que reciben la campana y el correo.</summary>
        public const string CLAVE_USUARIOS_DIRECCION = "Pedidos:AvisoPromesaRiesgo:UsuariosDireccion";
        public const string USUARIOS_DIRECCION_POR_DEFECTO = "Carlos,Manuel";

        /// <summary>Para los tests (null = el Web.config).</summary>
        internal static Func<string, string> LeerAjuste { get; set; }

        public static ModoAvisoPromesaRiesgo Modo => Interpretar(Leer(CLAVE));

        internal static ModoAvisoPromesaRiesgo Interpretar(string valor)
        {
            switch (valor?.Trim().ToUpperInvariant())
            {
                case "SOMBRA":
                    return ModoAvisoPromesaRiesgo.Sombra;
                case "ACTIVO":
                    return ModoAvisoPromesaRiesgo.Activo;
                default:
                    return ModoAvisoPromesaRiesgo.Apagado;
            }
        }

        public static List<string> UsuariosDireccion()
        {
            string valor = Leer(CLAVE_USUARIOS_DIRECCION);
            if (string.IsNullOrWhiteSpace(valor))
            {
                valor = USUARIOS_DIRECCION_POR_DEFECTO;
            }
            var usuarios = new List<string>();
            foreach (string usuario in valor.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string limpio = usuario.Trim();
                if (limpio.Length > 0 && !usuarios.Contains(limpio, StringComparer.OrdinalIgnoreCase))
                {
                    usuarios.Add(limpio);
                }
            }
            return usuarios;
        }

        private static string Leer(string clave)
        {
            try
            {
                return LeerAjuste != null ? LeerAjuste(clave) : ConfigurationManager.AppSettings[clave];
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// NestoAPI#693: algo que ha pasado con un producto y que puede explicar por qué un pedido ya no llega a su fecha. Lo pone
    /// quien lanza el evento (la reposición que se termina, el pedido a proveedor que se retrasa, otro pedido que lo lleva);
    /// el texto final lo compone <see cref="TextosPromesaRiesgo.TextoCausa"/>, con lo que necesita cada pedido.
    /// </summary>
    public class CausaPromesaRiesgo
    {
        /// <summary>Texto ya hecho («el pedido a proveedor 4521 retrasa la fecha prevista del 22/10 al 30/10»). Opcional.</summary>
        public string Texto { get; set; }
        /// <summary>Reposición terminada: la tienda que la manda (REI, ALC).</summary>
        public string Tienda { get; set; }
        /// <summary>Reposición terminada: unidades que manda la tienda.</summary>
        public int? Mandadas { get; set; }
        /// <summary>Reposición terminada: número de traspaso.</summary>
        public int? Traspaso { get; set; }
        /// <summary>Pedido grabado o modificado: su número (para no culparle de su propio retraso).</summary>
        public int? PedidoOrigen { get; set; }
    }

    /// <summary>
    /// NestoAPI#693: un evento que puede romper promesas (va como argumento del job de Hangfire: serializable). Solo se
    /// recalculan los pedidos pendientes con fecha prometida que llevan alguno de <see cref="Productos"/>.
    /// </summary>
    public class EventoPromesaRiesgo
    {
        public string Empresa { get; set; } = Constantes.Empresas.EMPRESA_POR_DEFECTO;
        /// <summary>Qué ha pasado, para la columna Evento («reposición 81094 de Reina», «movimiento de stock (diario General)»).</summary>
        public string Descripcion { get; set; }
        public List<string> Productos { get; set; } = new List<string>();
        /// <summary>Producto → causa (opcional). Clave sin espacios.</summary>
        public Dictionary<string, CausaPromesaRiesgo> Causas { get; set; } = new Dictionary<string, CausaPromesaRiesgo>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>NestoAPI#693: un pedido pendiente con fecha prometida (candidato a recalcular).</summary>
    public class PedidoConPromesa
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public DateTime FechaPrometida { get; set; }
    }

    /// <summary>NestoAPI#693: una fila de PedidosPromesaRiesgo.</summary>
    public class RiesgoPromesa
    {
        public int Id { get; set; }
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public DateTime FechaPrometida { get; set; }
        /// <summary>Null = sin fecha.</summary>
        public DateTime? FechaNueva { get; set; }
        /// <summary>Qué productos fallan y por qué (texto para el usuario).</summary>
        public string Motivo { get; set; }
        /// <summary>El evento que lo detectó (o lo empeoró por última vez).</summary>
        public string Evento { get; set; }
        public DateTime Detectado { get; set; }
        public DateTime Actualizado { get; set; }
        /// <summary>Cuándo se avisó por la campana (null = sin avisar, o en Sombra).</summary>
        public DateTime? AvisadoCampana { get; set; }
        /// <summary>Cuándo salió en el correo resumen (null = aún no).</summary>
        public DateTime? AvisadoCorreo { get; set; }
        public DateTime? Resuelto { get; set; }
        public string MotivoResolucion { get; set; }
    }
}
