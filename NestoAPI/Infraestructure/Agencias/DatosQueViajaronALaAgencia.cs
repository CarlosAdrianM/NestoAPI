using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.Agencias
{
    /// <summary>
    /// NestoAPI#725 (Carlos, 28/10/26, envío 249990): «nunca una cosa en nuestra base de datos y otra en la de la agencia».
    /// Los datos de un envío que viajan a la agencia al REGISTRARLO (CTT e Innovatrans: al imprimir la etiqueta), es decir,
    /// los de <see cref="DatosEnvioRemoto.DesdeEnvio"/> que leen <c>AgenciaRemotaCTT.ConstruirManifiesto</c> y
    /// <c>AgenciaRemotaInnovatrans</c>. Si cambia alguno de ellos en un envío ya registrado sin reenviarlo, la agencia no se
    /// entera. El horario no está: no viaja a ninguna de las dos (solo lo usa GLS, que manda los datos al cierre).
    /// Pura, para testear sin base de datos.
    /// </summary>
    public static class DatosQueViajaronALaAgencia
    {
        private static readonly CultureInfo CASTELLANO = new CultureInfo("es-ES");

        /// <summary>Un dato que viajó a la agencia y que la petición cambia: el nombre para el usuario y los dos valores en texto.</summary>
        public class CampoCambiado
        {
            public string Campo { get; set; }
            public string Anterior { get; set; }
            public string Nuevo { get; set; }
        }

        private class DatoQueViaja
        {
            public string Nombre { get; set; }
            /// <summary>Valor normalizado para comparar y enseñar (cadenas sin espacios, reembolso sin el centinela negativo).</summary>
            public Func<EnviosAgencia, string> Valor { get; set; }
        }

        private static readonly List<DatoQueViaja> DATOS = new List<DatoQueViaja>
        {
            new DatoQueViaja { Nombre = "Reembolso", Valor = e => Math.Max(0M, e.Reembolso).ToString("C", CASTELLANO) },
            new DatoQueViaja { Nombre = "Retorno", Valor = e => e.Retorno.ToString(CultureInfo.InvariantCulture) },
            new DatoQueViaja { Nombre = "Servicio", Valor = e => e.Servicio.ToString(CultureInfo.InvariantCulture) },
            new DatoQueViaja { Nombre = "Nombre", Valor = e => Texto(e.Nombre) },
            new DatoQueViaja { Nombre = "Dirección", Valor = e => Texto(e.Direccion) },
            new DatoQueViaja { Nombre = "Código postal", Valor = e => Texto(e.CodPostal) },
            new DatoQueViaja { Nombre = "Población", Valor = e => Texto(e.Poblacion) },
            new DatoQueViaja { Nombre = "País", Valor = e => e.Pais.ToString(CultureInfo.InvariantCulture) },
            new DatoQueViaja { Nombre = "Teléfono", Valor = e => Texto(e.Telefono) },
            new DatoQueViaja { Nombre = "Móvil", Valor = e => Texto(e.Movil) },
            new DatoQueViaja { Nombre = "Email", Valor = e => Texto(e.Email) },
            new DatoQueViaja { Nombre = "Bultos", Valor = e => e.Bultos.ToString(CultureInfo.InvariantCulture) },
            new DatoQueViaja { Nombre = "Peso", Valor = e => e.Peso.ToString("0.###", CASTELLANO) },
            new DatoQueViaja { Nombre = "Observaciones", Valor = e => Texto(e.Observaciones) }
        };

        /// <summary>Los nombres de los datos que viajan a la agencia al registrar, en el orden en que se comparan.</summary>
        public static IReadOnlyList<string> Campos => DATOS.Select(d => d.Nombre).ToList();

        /// <summary>
        /// ¿El envío (tal como está en la BD) ya está registrado en una agencia de gestión remota? Tiene albarán y está en
        /// curso o más. Las agencias que tramitan al cierre (GLS) no cuentan: allí los datos viajan al cerrar.
        /// </summary>
        public static bool EstaRegistradoEnLaAgencia(string codigoBarras, short estado, bool agenciaConGestionRemota)
            => agenciaConGestionRemota && !string.IsNullOrWhiteSpace(codigoBarras) && estado >= Constantes.Agencias.ESTADO_EN_CURSO;

        /// <summary>Nombres de los datos que viajaron a la agencia y que cambian entre lo registrado y lo que se envía.</summary>
        public static List<string> Comparar(EnviosAgencia registrado, EnviosAgencia enviado)
            => Diferencias(registrado, enviado).Select(c => c.Campo).ToList();

        /// <summary>Como <see cref="Comparar"/>, con los valores anterior y nuevo de cada dato (para el historial).</summary>
        public static List<CampoCambiado> Diferencias(EnviosAgencia registrado, EnviosAgencia enviado)
        {
            List<CampoCambiado> cambios = new List<CampoCambiado>();
            if (registrado == null || enviado == null)
            {
                return cambios;
            }
            foreach (DatoQueViaja dato in DATOS)
            {
                string anterior = dato.Valor(registrado);
                string nuevo = dato.Valor(enviado);
                if (!string.Equals(anterior, nuevo, StringComparison.Ordinal))
                {
                    cambios.Add(new CampoCambiado { Campo = dato.Nombre, Anterior = anterior, Nuevo = nuevo });
                }
            }
            return cambios;
        }

        /// <summary>El texto de alarma del 409 (de usted).</summary>
        public static string MensajeCambioSinAgencia(int numeroEnvio, IEnumerable<string> campos)
            => $"El envío {numeroEnvio} ya está registrado en la agencia. Este cambio ({string.Join(", ", campos)}) se haría en NUESTRO sistema " +
               "pero NO en el de la agencia. Solo debe hacerse si ha hablado con la agencia y le han confirmado que lo cambian en su sistema. " +
               "Si quiere que la agencia reciba el cambio, use «Modificar datos y reenviar a la agencia».";

        private static string Texto(string valor) => valor?.Trim() ?? string.Empty;
    }
}
