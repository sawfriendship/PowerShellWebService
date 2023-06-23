// Please see documentation at https://docs.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.
var test = 0;

var min_id = 0;
var max_id = 0;
var table = $('#LogTable table')
var thead = $('<thead><tr><th>id</th><th>BeginDate</th><th>UserName</th><th>IPAddress</th><th>Wrapper</th><th>Script</th><th>Method</th><th>Success</th><th>HadErrors</th><th>Error</th></tr></thead>')
var tbody = $('<tbody></tbody>')
table.append(thead);
table.append(tbody)

function load_modal(event_id) {
    $('#Modals').empty()
    var _new_modal = $('#ModalTemplate').clone().addClass('_new_modal').appendTo('#Modals')
    _new_modal.find('.modal-title').html(`Event ${event_id}`)

    $.ajax({
        url: `/log/${event_id}`,
        method: 'GET',
        success: function(responce) {
            console.log(responce)
            var jString = JSON.stringify(responce, null, 2);
            if (responce.Success && responce.Data) {
                window.__renderJSONToHTMLTable({data: responce.Data, rootElement: '._new_modal .modal-body'});
            }

            $('._new_modal .modal-body').append($(
                `
                <div id="Result" class="">
                    <h3>Raw data</h3>
                    <textarea id="_result" class="form-control bg-dark text-light border border-4 rounded rounded-5" rows="20" placeholder="Result" readonly>${jString}</textarea>
                    <span class="btn btn-dark _btn_copy"><i class="bi bi-clipboard"></i></span>
                </div>
                `
            ));

            $('._btn_copy').on('click', ()=>navigator.clipboard.writeText($('#_result').val()));
            console.log('log-ok')

            $.ajax({
                url: `/transcript/${event_id}`,
                method: 'GET',
                success: function(responce) {
                    $('._new_modal .modal-body').append($(
                        `
                        <div id="Result" class="">
                            <h3>Transcript</h3>
                            <textarea id="_result" class="form-control bg-dark text-light border border-4 rounded rounded-5" rows="20" placeholder="Result" readonly>${responce}</textarea>
                            <span class="btn btn-dark _btn_copy"><i class="bi bi-clipboard"></i></span>
                        </div>
                        `
                    ));
                    $('._btn_copy').on('click', ()=>navigator.clipboard.writeText($('#_result').val()));
                    console.log('transcript-ok')
                    console.log(data)
        
                },
                error: function(responce) {
                    console.log('transcript-err')
                }
            })
        },
        error: function(responce) {
            console.log('log-err')
        }
    });

    $('._new_modal').modal('show')
}

function load_data(asc=0) {
    var form_interval = $('form #_load_param [name=_interval]')
    var form_count = $('form #_load_param [name=_count]')
    var filter = {}
    if (asc < 0.5) {
        d_op = '>'
        d_val = max_id
    } else {
        d_op = '<'
        d_val = min_id
    }
    $.ajax({
        url: `/log?asc=${asc}&interval=${form_interval.val()}&count=${form_count.val()}`,
        method: 'POST',
        contentType: 'application/json',
        data: `[{"column":"id","operator":"${d_op}","value":"${d_val}"}]`,
        success: function(responce) {
            var data = responce.Data;
            if (responce.Success && responce.Count > 0) {
                var row = '';
                if (max_id == 0 || min_id == 0) {
                    $('#ParamURL').html($(`<p>URL: <a href="${this.url}" target="_blank">${this.url}</a></p>`))
                };
                data.forEach(function(item){
                    var sclass = 'table-light';
                    if (item.HadErrors) {sclass = 'table-warning'}
                    if (!item.Success) {sclass = 'table-danger'}
                    var row = $(`<tr row_id="${item.id}" class="${sclass}" style="display:none"><td>${item.id}</td><td>${item.BeginDate}</td><td>${item.UserName}</td><td>${item.IPAddress}</td><td>${item.Wrapper}</td><td>${item.Script}</td><td>${item.Method}</td><td>${item.Success}</td><td>${item.HadErrors}</td><td>${item.Error}</td></tr>`)
                    if (max_id == 0 || min_id == 0) {
                        min_id = item.id
                        max_id = item.id
                    }
                    if (item.id > max_id) {
                        // console.log('>')
                        max_id = item.id
                        tbody.prepend(row)
                    } else if (item.id < min_id) {
                        // console.log('<')
                        min_id = item.id
                        tbody.append(row)
                    } else {}
                    row.show(320)
                    
                })
            }
            // console.log('ok')
        },
        error: function(responce) {
            // console.log('err')
        },
        complete: function(){
            if (max_id == 0 || min_id == 0) {
                // $('#ParamURL').html($(`<p>URL: <a href="${this.url}" target="_blank">${this.url}</a></p>`))
            }
        },
    });

}

// https://doka.guide/js/infinite-scroll/?ysclid=li4zwmwam8358176772

function checkPosition() {
    // Нам потребуется знать высоту документа и высоту экрана:
    const height = document.body.offsetHeight
    const screenHeight = window.innerHeight
    // Они могут отличаться: если на странице много контента,
    // высота документа будет больше высоты экрана (отсюда и скролл).

    // Записываем, сколько пикселей пользователь уже проскроллил:
    const scrolled = window.scrollY

    // Обозначим порог, по приближении к которому
    // будем вызывать какое-то действие.
    // В нашем случае — четверть экрана до конца страницы:
    const threshold = height - screenHeight / 4

    // Отслеживаем, где находится низ экрана относительно страницы:
    const position = scrolled + screenHeight

    if (position >= threshold) {
        // Если мы пересекли полосу-порог, вызываем нужное действие.
        load_data(-1)
    }
}

function throttle(callee, timeout) {
    let timer = null
    return function perform(...args) {
        if (timer) return
        timer = setTimeout(() => {
            callee(...args)
            clearTimeout(timer)
            timer = null
        }, timeout)
    }
}

var reload_timer_id = 0;

$(document).ready(function() {
    var form_interval = $('form #_load_param [name=_interval]')
    if (form_interval.val() > 0) {
        reload_timer_id = setInterval(() => load_data(1), form_interval.val()*1000);
    }
    form_interval.on('click change keyup', function(){
        clearInterval(reload_timer_id);
        reload_timer_id = setInterval(() => load_data(1), form_interval.val()*1000);
    });

    min_id = 0;
    max_id = 0;

    load_data(0);
    // load_param();
    
    $('#LogTable tbody').on('click', 'tr', function(e){var row_id = $(this).attr('row_id');load_modal(row_id);});
    $('#Modals').on('click', '._modal_close', function(e){$('._new_modal').modal('hide');});
    $('._btn_load_more').on('click', function(e){load_data(-1);});
    window.addEventListener('scroll', throttle(checkPosition, 250))
});
