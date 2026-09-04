using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Contabilidad;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models.Pagos;
using System;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#181: cobro con tarjeta guardada autenticado con EMV 3DS 2, sin que el cliente vea
    /// la pasarela.
    ///
    /// <para>La página que sirve este controlador se abre en el WebView que la app ya tiene
    /// (PagoRedsysWebView). Recoge los datos reales del navegador —que son los que hacen que el
    /// emisor se atreva a no preguntar—, ejecuta el 3DSMethod en un iframe oculto y solo pinta
    /// algo si el banco exige desafío. Si sale frictionless, el cliente pasa de "confirmar
    /// pedido" a "pedido pagado" sin ver nada por el camino.</para>
    ///
    /// <para><b>Sin estado en el servidor</b>: entre llamada y llamada, los valores efímeros del
    /// 3DS (protocolVersion, threeDSServerTransID) los guarda la página. No son secretos ni
    /// autorizan nada: el importe, el cliente y el número de orden se releen del PagoTPV por su
    /// TokenAcceso, que es lo único que da permiso, y la tarjeta se valida contra el cliente de
    /// ese pago.</para>
    ///
    /// <para>El cobro autorizado entra como prepago del pedido por la notificación de Redsys de
    /// siempre (ProcesarNotificacion): este flujo cambia CÓMO se autentica, no lo que pasa
    /// después.</para>
    /// </summary>
    [RoutePrefix("pago3ds")]
    [AllowAnonymous]
    public class Pago3DSController : ApiController
    {
        private readonly IServicioPagos _servicioPagos;

        public Pago3DSController()
        {
            _servicioPagos = new ServicioPagos(new RedsysService(), new ContabilidadService(),
                new LectorParametrosUsuario());
        }

        public Pago3DSController(IServicioPagos servicioPagos)
        {
            _servicioPagos = servicioPagos;
        }

        /// <summary>
        /// La página de pago. URL: https://api.nuevavision.es/pago3ds/{tokenAcceso}/{tarjetaId}
        /// </summary>
        [HttpGet]
        [Route("{token:guid}/{tarjetaId:int}")]
        public HttpResponseMessage Pagina(Guid token, int tarjetaId)
        {
            string html = PAGINA
                .Replace("@@TOKEN@@", token.ToString())
                .Replace("@@TARJETA@@", tarjetaId.ToString());

            return Html(html);
        }

        /// <summary>Paso 1: qué sabe hacer la tarjeta.</summary>
        [HttpPost]
        [Route("{token:guid}/{tarjetaId:int}/iniciar")]
        public async Task<IHttpActionResult> Iniciar(Guid token, int tarjetaId)
        {
            InicioAutenticacion3DS inicio = await _servicioPagos.Iniciar3DS(token, tarjetaId)
                .ConfigureAwait(false);
            return Ok(inicio);
        }

        /// <summary>Paso 2: autenticar. Devuelve autorizado, denegado o el desafío que pintar.</summary>
        [HttpPost]
        [Route("{token:guid}/{tarjetaId:int}/autenticar")]
        public async Task<IHttpActionResult> Autenticar(Guid token, int tarjetaId,
            PeticionAutenticacion3DS peticion)
        {
            ResultadoAutenticacion3DS resultado = await _servicioPagos
                .Autenticar3DS(token, tarjetaId, peticion).ConfigureAwait(false);

            return Ok(Traducir(resultado));
        }

        /// <summary>
        /// Donde el emisor avisa de que ha terminado el 3DSMethod. Lo carga un iframe oculto:
        /// solo tiene que avisar a la página de que ya puede seguir.
        /// </summary>
        [HttpPost]
        [Route("{token:guid}/metodo")]
        public HttpResponseMessage Metodo(Guid token)
        {
            return Html("<!doctype html><html><body><script>"
                + "try{parent.postMessage('3dsMethodDone','*');}catch(e){}"
                + "</script></body></html>");
        }

        /// <summary>
        /// Paso 3: donde el ACS del emisor deja el resultado del desafío (cres). Llega como POST
        /// de formulario desde el iframe del desafío, así que la respuesta tiene que sacar al
        /// cliente del iframe y llevarlo a la página de resultado.
        /// </summary>
        [HttpPost]
        [Route("{token:guid}/{tarjetaId:int}/reto")]
        public async Task<HttpResponseMessage> Reto(Guid token, int tarjetaId)
        {
            NameValueCollection formulario = await Request.Content.ReadAsFormDataAsync().ConfigureAwait(false);
            string cres = formulario?["cres"];
            // El ACS solo devuelve el cres, asi que la version del protocolo viaja en la propia
            // notificationURL que le dimos (ServicioPagos.UrlReto3DS)
            string protocolVersion = Request.GetQueryNameValuePairs()
                .FirstOrDefault(p => string.Equals(p.Key, "pv", StringComparison.OrdinalIgnoreCase)).Value;

            ResultadoAutenticacion3DS resultado = await _servicioPagos
                .ResolverReto3DS(token, tarjetaId, protocolVersion, cres).ConfigureAwait(false);

            string destino = resultado.Estado == EstadoAutenticacion3DS.Autorizado
                ? URL_OK
                : URL_KO;

            // window.top: el cres llega dentro del iframe del desafío, y hay que salir de él
            return Html("<!doctype html><html><body><script>"
                + $"window.top.location.href='{destino}';"
                + "</script></body></html>");
        }

        private const string URL_OK = "/pago/ok.html";
        private const string URL_KO = "/pago/ko.html";

        /// <summary>
        /// El estado va como texto a propósito: un enum serializado como número es una bomba de
        /// relojería el día que alguien reordene los valores.
        /// </summary>
        private static object Traducir(ResultadoAutenticacion3DS resultado)
        {
            return new
            {
                Estado = resultado.Estado.ToString(),
                resultado.AcsUrl,
                resultado.Creq,
                resultado.ProtocolVersion,
                resultado.MensajeError
            };
        }

        private static HttpResponseMessage Html(string html)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html, Encoding.UTF8, "text/html")
            };
        }

        private const string PAGINA = @"<!doctype html>
<html lang=""es"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<title>Confirmando el pago</title>
<style>
  html,body{height:100%;margin:0;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;background:#f5f5f5;color:#333}
  #espera{display:flex;flex-direction:column;align-items:center;justify-content:center;height:100%;text-align:center;padding:0 24px}
  .spinner{border:4px solid #e6e6e6;border-top:4px solid #007AFF;border-radius:50%;width:44px;height:44px;animation:giro 1s linear infinite;margin-bottom:18px}
  @keyframes giro{to{transform:rotate(360deg)}}
  #reto{display:none;position:fixed;inset:0;width:100%;height:100%;border:0;background:#fff}
  iframe#metodo{display:none}
  p{margin:0;font-size:15px}
  small{color:#888;margin-top:8px}
</style>
</head>
<body>
<div id=""espera"">
  <div class=""spinner""></div>
  <p>Confirmando el pago con tu banco...</p>
  <small>No cierres esta pantalla</small>
</div>

<iframe id=""metodo"" name=""metodo""></iframe>
<iframe id=""reto"" name=""reto""></iframe>
<form id=""oculto"" style=""display:none""></form>

<script>
(function(){
  var BASE = '/pago3ds/@@TOKEN@@/@@TARJETA@@';
  var OK = '/pago/ok.html';
  var KO = '/pago/ko.html';

  function post(url, cuerpo){
    return fetch(url, {
      method:'POST',
      headers:{'Content-Type':'application/json'},
      body: JSON.stringify(cuerpo || {})
    }).then(function(r){ return r.json(); });
  }

  function enviarFormulario(url, campos, destino){
    var f = document.getElementById('oculto');
    f.innerHTML = '';
    f.method = 'POST';
    f.action = url;
    if (destino) { f.target = destino; }
    for (var clave in campos){
      if (!campos.hasOwnProperty(clave) || campos[clave] == null) { continue; }
      var i = document.createElement('input');
      i.type = 'hidden'; i.name = clave; i.value = campos[clave];
      f.appendChild(i);
    }
    f.submit();
  }

  // El 3DSMethod deja que el emisor mire el dispositivo: sube mucho el frictionless, pero es
  // opcional y no puede bloquear el pago. Se le da un margen corto y se sigue.
  function ejecutarMetodo(inicio){
    return new Promise(function(resuelve){
      var terminado = false;
      function acabar(valor){
        if (terminado) { return; }
        terminado = true;
        window.removeEventListener('message', alRecibir);
        resuelve(valor);
      }
      function alRecibir(e){
        if (e && e.data === '3dsMethodDone') { acabar('Y'); }
      }
      window.addEventListener('message', alRecibir);
      setTimeout(function(){ acabar('N'); }, 4000);
      enviarFormulario(inicio.ThreeDSMethodURL,
        { threeDSMethodData: inicio.ThreeDSMethodData }, 'metodo');
    });
  }

  function datosNavegador(){
    return {
      UserAgent: navigator.userAgent,
      AcceptHeader: 'text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8',
      Idioma: navigator.language || 'es-ES',
      ProfundidadColor: screen.colorDepth || 24,
      AltoPantalla: screen.height || 0,
      AnchoPantalla: screen.width || 0,
      DiferenciaHorariaMinutos: new Date().getTimezoneOffset(),
      JavaScriptActivado: true
    };
  }

  function pintarReto(resultado){
    document.getElementById('espera').style.display = 'none';
    document.getElementById('reto').style.display = 'block';
    enviarFormulario(resultado.AcsUrl, { creq: resultado.Creq }, 'reto');
  }

  function terminar(url){ window.location.href = url; }

  post(BASE + '/iniciar').then(function(inicio){
    if (!inicio || !inicio.Soporta3DS2){
      // Plan de reserva: la pasarela de siempre. El cliente ve Redsys, pero paga.
      if (inicio && inicio.FormularioClasico){
        enviarFormulario(inicio.FormularioClasico.Url, {
          Ds_SignatureVersion: inicio.FormularioClasico.Ds_SignatureVersion,
          Ds_MerchantParameters: inicio.FormularioClasico.Ds_MerchantParameters,
          Ds_Signature: inicio.FormularioClasico.Ds_Signature
        });
        return null;
      }
      terminar(KO);
      return null;
    }

    var metodo = inicio.ThreeDSMethodURL
      ? ejecutarMetodo(inicio)
      : Promise.resolve('N');

    return metodo.then(function(comp){
      return post(BASE + '/autenticar', {
        ProtocolVersion: inicio.ProtocolVersion,
        ThreeDSServerTransID: inicio.ThreeDSServerTransID,
        ThreeDSCompInd: comp,
        Navegador: datosNavegador()
      });
    }).then(function(resultado){
      if (!resultado){ terminar(KO); return; }
      if (resultado.Estado === 'RetoRequerido'){ pintarReto(resultado); return; }
      terminar(resultado.Estado === 'Autorizado' ? OK : KO);
    });
  }).catch(function(){
    terminar(KO);
  });
})();
</script>
</body>
</html>";
    }
}
