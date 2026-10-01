using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.Agencias.Gls
{
    /// <summary>Una petición a GLS tal como la guardó Nesto en AgenciasLlamadasWeb.</summary>
    public class PeticionGlsGuardada
    {
        public int Llamada { get; set; }
        public DateTime Fecha { get; set; }
        public string Usuario { get; set; }
        public string Cuerpo { get; set; }
    }

    /// <summary>Lo que necesita la API para construir la petición de un envío.</summary>
    public class EnvioParaPeticionGls
    {
        public EnviosAgencia Envio { get; set; }
        public Empresa Empresa { get; set; }
        public string IdentificadorAgencia { get; set; }
    }

    public class ComparacionPeticionGls
    {
        public int Llamada { get; set; }
        public DateTime Fecha { get; set; }
        public string Usuario { get; set; }
        public string CodigoBarras { get; set; }
        /// <summary>Null si el código de barras ya no está en EnviosAgencia.</summary>
        public int? Envio { get; set; }
        public List<DiferenciaPeticionGls> Diferencias { get; set; } = new List<DiferenciaPeticionGls>();
    }

    public class ResultadoSombraGls
    {
        public int Peticiones { get; set; }
        public int Iguales { get; set; }
        public int ConDiferencias { get; set; }
        public int SinEnvio { get; set; }
        /// <summary>Cuerpos guardados con Agencia = ASM que no son un GrabaServicios (p. ej. consultas de seguimiento).</summary>
        public int Ignoradas { get; set; }
        /// <summary>
        /// Peticiones cuya fecha ya no está en la BD: al tramitar, el envío pasa a tener la fecha de ese día
        /// (TramitacionEnviosService), así que si Nesto mandó otra (el envío se creó antes) no se puede comparar.
        /// </summary>
        public int FechasReescritasAlTramitar { get; set; }
        /// <summary>Cuántas peticiones difieren en cada campo: lo primero que hay que mirar.</summary>
        public Dictionary<string, int> CamposConDiferencias { get; set; } = new Dictionary<string, int>();
        /// <summary>Solo las que no coinciden (o no tienen envío).</summary>
        public List<ComparacionPeticionGls> Detalle { get; set; } = new List<ComparacionPeticionGls>();
    }

    /// <summary>
    /// NestoAPI#552 (fase 1, sombra): para las peticiones que Nesto mandó a GLS, la API construye la suya con el envío
    /// tal como está HOY en EnviosAgencia y las compara. No manda nada a GLS ni escribe en la BD. Ojo al leer el
    /// resultado: si alguien cambió el envío después de tramitarlo (dirección, bultos...), esa diferencia es real
    /// pero no es un fallo de la API.
    /// </summary>
    public class SombraPeticionesGls
    {
        public const string AGENCIA_LLAMADAS = "ASM";
        public const int MAXIMO_DIAS = 31;

        private readonly NVEntities _db;

        public SombraPeticionesGls(NVEntities db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<ResultadoSombraGls> Ejecutar(DateTime desde, DateTime hasta)
        {
            if (hasta < desde || (hasta - desde).TotalDays > MAXIMO_DIAS)
            {
                throw new Exceptions.NestoBusinessException($"El periodo de la sombra de GLS tiene que ir hacia delante y no pasar de {MAXIMO_DIAS} días.");
            }

            List<PeticionGlsGuardada> peticiones = await _db.AgenciasLlamadasWeb.AsNoTracking()
                .Where(l => l.Agencia == AGENCIA_LLAMADAS && l.Exito && l.Fecha >= desde && l.Fecha < hasta)
                .OrderBy(l => l.Id)
                .Select(l => new PeticionGlsGuardada { Llamada = l.Id, Fecha = l.Fecha, Usuario = l.Usuario, Cuerpo = l.CuerpoLlamada })
                .ToListAsync().ConfigureAwait(false);

            List<string> codigos = CodigosDeBarras(peticiones);
            List<EnviosAgencia> envios = codigos.Count == 0
                ? new List<EnviosAgencia>()
                : await _db.EnviosAgencias.AsNoTracking().Where(e => codigos.Contains(e.CodigoBarras)).ToListAsync().ConfigureAwait(false);
            List<string> empresas = envios.Select(e => e.Empresa).Distinct().ToList();
            List<int> agencias = envios.Select(e => e.Agencia).Distinct().ToList();
            Dictionary<string, Empresa> empresasPorNumero = (await _db.Empresas.AsNoTracking().Where(e => empresas.Contains(e.Número)).ToListAsync().ConfigureAwait(false))
                .ToDictionary(e => e.Número.Trim());
            Dictionary<int, string> identificadores = await _db.AgenciasTransportes.AsNoTracking()
                .Where(a => agencias.Contains(a.Numero))
                .ToDictionaryAsync(a => a.Numero, a => a.Identificador).ConfigureAwait(false);

            Dictionary<string, EnviosAgencia> enviosPorCodigo = envios
                .GroupBy(e => e.CodigoBarras.Trim())
                .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.Numero).First());

            return Comparar(peticiones, codigo =>
            {
                if (!enviosPorCodigo.TryGetValue(codigo, out EnviosAgencia envio) ||
                    !empresasPorNumero.TryGetValue(envio.Empresa.Trim(), out Empresa empresa))
                {
                    return null;
                }
                identificadores.TryGetValue(envio.Agencia, out string identificador);
                return new EnvioParaPeticionGls { Envio = envio, Empresa = empresa, IdentificadorAgencia = identificador };
            });
        }

        public static List<string> CodigosDeBarras(IEnumerable<PeticionGlsGuardada> peticiones)
        {
            return peticiones
                .Select(p => CodigoDeBarras(PeticionGls.ServiciosDeLaPeticion(p.Cuerpo)))
                .Where(c => !string.IsNullOrEmpty(c))
                .Distinct()
                .ToList();
        }

        public static ResultadoSombraGls Comparar(IEnumerable<PeticionGlsGuardada> peticiones, Func<string, EnvioParaPeticionGls> buscarEnvio)
        {
            var resultado = new ResultadoSombraGls();
            foreach (PeticionGlsGuardada peticion in peticiones)
            {
                XElement nesto = PeticionGls.ServiciosDeLaPeticion(peticion.Cuerpo);
                string codigo = CodigoDeBarras(nesto);
                if (string.IsNullOrEmpty(codigo))
                {
                    resultado.Ignoradas++;
                    continue;
                }

                resultado.Peticiones++;
                var comparacion = new ComparacionPeticionGls
                {
                    Llamada = peticion.Llamada,
                    Fecha = peticion.Fecha,
                    Usuario = peticion.Usuario,
                    CodigoBarras = codigo
                };

                EnvioParaPeticionGls datos = buscarEnvio(codigo);
                if (datos?.Envio == null)
                {
                    resultado.SinEnvio++;
                    resultado.Detalle.Add(comparacion);
                    continue;
                }

                comparacion.Envio = datos.Envio.Numero;
                comparacion.Diferencias = ComparadorPeticionesGls.Comparar(nesto, PeticionGls.ConstruirServicios(datos.Envio, datos.Empresa, datos.IdentificadorAgencia));
                if (comparacion.Diferencias.RemoveAll(d => EsFechaReescritaAlTramitar(d, peticion.Fecha)) > 0)
                {
                    resultado.FechasReescritasAlTramitar++;
                }
                if (comparacion.Diferencias.Count == 0)
                {
                    resultado.Iguales++;
                    continue;
                }

                resultado.ConDiferencias++;
                resultado.Detalle.Add(comparacion);
                foreach (string campo in comparacion.Diferencias.Select(d => d.Campo))
                {
                    resultado.CamposConDiferencias[campo] = resultado.CamposConDiferencias.TryGetValue(campo, out int n) ? n + 1 : 1;
                }
            }
            return resultado;
        }

        private static bool EsFechaReescritaAlTramitar(DiferenciaPeticionGls diferencia, DateTime fechaLlamada)
            => diferencia.Campo == ComparadorPeticionesGls.CAMPO_FECHA
                && ComparadorPeticionesGls.LeerFecha(diferencia.Api, out DateTime fechaActual)
                && fechaActual.Date == fechaLlamada.Date;

        private static string CodigoDeBarras(XElement servicios)
            => servicios?.Element(PeticionGls.Ns + "Envio")?.Attribute("codbarras")?.Value?.Trim();
    }
}
