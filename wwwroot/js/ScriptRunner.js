// Please see documentation at https://docs.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

function load_wrapper_form() {
    var PwShUrl = $('#conf input[name=PwShUrl]').val();
    $('#_wrapper').html($(`<option value="">---</option>`));
    $.ajax({
        url: `/${PwShUrl}/`,
        method: 'GET',
        cache: true,
        contentType: 'application/json; charset=utf-8',
        success: function(responce){
			if (responce.Success) {
				var data = responce.Data
				for (var k in data) {$(`<option value="${k}">${k}</option>`).appendTo('#_wrapper')}
				if ('wrapper' in localStorage) {
					var wrapper = localStorage['wrapper']
					if (wrapper) {
						$('#_wrapper').val(wrapper)
						$('#_script').prop('disabled', false)
					}

					load_script_form(wrapper);

					if ('script' in localStorage) {
						var script = localStorage['script']
						$('#_script').val(script)
					}
					
				}
			}
        },
        error: function(responce){
            $('#_script').prop('disabled', true)
        }
    })

}

function load_script_form(wrapper) {
    var PwShUrl = $('#conf input[name=PwShUrl]').val();
    $('#_script').html($(`<option value="">---</option>`));
    if (wrapper) {
        $.ajax({
            url: `/${PwShUrl}/${wrapper}`,
            method: 'GET',
            cache: true,
            contentType: 'application/json; charset=utf-8',
            success: function(responce){
				if (responce.Success) {
					var data = responce.Data
					for (var k in data) {$(`<option value="${data[k]}">${data[k]}</option>`).appendTo('#_script')}
					if ('script' in localStorage) {
						var script = localStorage['script']
						$('#_script').val(script)
					}

					$('#_script').prop('disabled', false)
					update_url();
				}
            },
            error: function(responce){
                $('#_script').prop('disabled', true)
            }
        })
    } else {
        $('#_script').prop('disabled', true)
    }
        
    if ('script' in localStorage) {$('#_script').val(localStorage['script'])}
}

function ani_send(d) {
    $({num: 0}).animate({num: 80}, {
        duration: d,
        easing: "swing",
        step: function(val) {
            int_val = Math.ceil(val)
            $('#_result').css('background',`linear-gradient(60deg, rgb(0,0,0), rgb(${int_val},${int_val},${int_val}), rgb(0,0,0))`);
        }
    });

    $({num: 80}).animate({num: 0}, {
        duration: d,
        easing: "swing",
        step: function(val) {
            int_val = Math.ceil(val)
            $('#_result').css('background',`linear-gradient(60deg, rgb(0,0,0), rgb(${int_val},${int_val},${int_val}), rgb(0,0,0))`);
        }
    });
}

function put_to_body() {
    var result = {};
    $('#Params').children().each(function(){
        var dict = {};
		var toggles = $(this).find('i[class="bi-toggle-on"]')
		if (toggles.length) {
			$(this).find('.param_prop').each(function(i,Prop){dict[Prop.name]=Prop.value});
			if (dict['Property']) {
				var Type = dict['Type']
				var Value = dict['Value'];
				if (Type == 'String') {
					v = Value
				} else if (Type == 'Number') {
					try {v = Number(Value)} catch (e) {v = Value}
				} else if (Type == 'Bool') {
					if (!Value) {v = false} else {try {v = Boolean(JSON.parse(Value.toLowerCase()))} catch (e) {v = Value}}
				} else if (Type == 'Object') {
					if (!Value) {v = {}} else {try {v = JSON.parse(Value)} catch (e) {v = Value}}
				}

				result[dict['Property']] = v;
			}
		}
    })
    var j_string = JSON.stringify(result);
    $('#_body').val(j_string)
}

function add_param() {$('#Param').clone().appendTo('#Params').show(200);} 

function del_param(item) {item.parent().hide(200, function(){$(this).remove(); put_to_body();});}

function toggle_param(item) {
	var property = item.parent().find('input[name=Property]').val();
	item.find('i').each(function(){
		if ($(this).hasClass('bi-toggle-on')) {
			console.log(`property '${property}' is off`);
			$(this).removeClass('bi-toggle-on').addClass('bi-toggle-off');
		} else if ($(this).hasClass('bi-toggle-off')) {
			console.log(`property '${property}' is on`);
			$(this).removeClass('bi-toggle-off').addClass('bi-toggle-on');
		} else {}
	})
};

function update_url() {
    var PwShUrl = $('#conf input[name=PwShUrl]').val();
    var url = `/${PwShUrl}`;
    var wrapper = $('#_wrapper').val()
    if (wrapper) {url = `${url}/${wrapper}`}
    var script = $('#_script').val()
    if (script) {url = `${url}/${script}.json`}
    $('a#pwsh_url').prop('href',url)
    $('a#pwsh_url').html(url)
    console.log(url)
}

function send_body() {
    var PwShUrl = $('#conf input[name=PwShUrl]').val();
    $('._btn_send').addClass('disabled')
    $('#_result').addClass('processing')
    $('#_result').removeClass('border-success _result_success border-danger _result_error')
    $('#_result ').val('...')
    ani_send(200);
    var method = $(this).attr('method')
    var depth = $('._depth').val()
    var outputtype = $('._outputtype').val()
    var wrapper = $('#_wrapper').val()
    var script = $('#_script').val()
    var url = `/${PwShUrl}/${wrapper}/${script}.json`
    var request_param = {
        url: url,
        method: method,
        headers: {maxDepth:depth},
        success: function(responce){
            t = responce
            $('._btn_send').removeClass('disabled')
            $('#_result').removeClass('processing')
            console.log(url,method,responce)
            if (outputtype == 'All') {
                var result = responce
            } else {
                var result = responce.Streams[outputtype]
            }
            var result_string = JSON.stringify(result, null, 2)
            $('#_result').val(result_string)
            if (!responce.Success || responce.Error || responce.Streams.HadErrors){
                $('#_result').removeClass('border-success _result_success').addClass('border-danger _result_error')
            } else {
                $('#_result').removeClass('border-danger _result_error').addClass('border-success _result_success')
            }
            ani_send(200);
        },
        error: function(responce){
            $('._btn_send').removeClass('disabled')
            $('#_result').removeClass('processing')
            $('#_result').removeClass('border-success _result_success').addClass('border-danger _result_error')
            ani_send(200);
        },
        complete: function(xhr, textStatus) {
            if (xhr.status == 200) {var responce_color = 'green'} else {var responce_color = 'red'}
            $('.StatusCode').html(`<span>${xhr.status}</span>`).css({color:responce_color})
        }
    }

    if (method != 'GET') {
        var j_string = $('#_body').val()
        if (j_string) {request_param['data'] = j_string}
        request_param['contentType'] = 'application/json; charset=utf-8'
    }

    $.ajax(request_param);
    
}

function write_to_clip() {navigator.clipboard.writeText($('#_result').val())}

function cache_reload() {$.ajax({url:'/reload',success:function(responce){load_wrapper_form();}})}
function cache_clear() {$.ajax({url:'/clear',success:function(responce){load_wrapper_form();}})}

$(document).ready(function() {
    load_wrapper_form();
    $('#_wrapper').on('change', function(e){var wrapper = $(this).val(); localStorage['wrapper'] = wrapper; load_script_form(wrapper);});
    $('#_script').on('change', function(e){var script = $(this).val(); localStorage['script'] = script; update_url()});
	$('#Params').on('click', '._btn_toggle_param', function(){toggle_param($(this))});
	$('#Params').on('click', '._btn_del_param', function(){del_param($(this))});
    $('#Params').bind('click keyup', put_to_body);
    $('._btn_add_param').bind('click', add_param);
    $('._btn_send').bind('click', send_body);
    $('._btn_copy').bind('click', write_to_clip);
    $('._btn_reload').bind('click', cache_reload);
    $('._btn_clear').bind('click', cache_clear);

});
