function JSONedtr( data, outputElement, config = {} ){

	if (! window.jQuery) {
        console.error("JSONedtr requires jQuery");
        return;
    }

	var JSONedtr = {};

	JSONedtr.config = config;

	if( JSONedtr.config.instantChange == null  )
		JSONedtr.config.instantChange = true ;

	JSONedtr.level = function ( node, lvl=0 ) {
		var output = '';

		$.each( node , function( key, value ) {
			JSONedtr.i++;

			if( typeof key == 'string' )
				key = key.replace(/\"/g,"&quot;");

			if( typeof value == 'object' ) {
				var type = typeof value;

				if( Array.isArray( value ) )
					type = 'array';

				output += '<div class="jse--row jse--row--array" id="jse--row-' + JSONedtr.i + '"><input type="text" class="jse--key jse--array" data-level="' + lvl + '" value="' + key + '"> : <span class="jse--typeof">(' + type + ')</span>';
				output += JSONedtr.level( value, lvl+1 );
				output += '<div class="jse--delete">✖</div></div>';
			} else {
				if( typeof value == 'string' )
					value = value.replace(/\"/g,"&quot;");
				output += '<div class="jse--row" id="jse--row-' + JSONedtr.i + '"><input type="text" class="jse--key" data-level="' + lvl + '" value="' + key + '"> : <span class="jse--typeof">(' + typeof value + ')</span><input type="text" class="jse--value" value="' + value + '" data-key="' + key + '"><div class="jse--delete">✖</div></div>';
			}
		})

		output += '<div class="jse--row jse--add" data-level="' + lvl + '"><button class="jse--plus">✚</button></div>';

		return output;
	}

	JSONedtr.getData = function( node = $( JSONedtr.outputElement + ' > .jse--row > input' ) ){
		var result = {};
		$.each( node, function() {

			if( $(this).hasClass( 'jse--value' ) ) {
				result[ $(this).data( 'key' ) ] = $(this).val();
			}

			if( $(this).hasClass( 'jse--object' ) || $(this).hasClass( 'jse--array' ) ) {
				var selector = '#' + $(this).parent().attr('id') + ' > .jse--row > input';
				result[ $(this).val( ) ] = JSONedtr.getData( $( selector ) );
			}
		});
		return result;
	}

	JSONedtr.getDataString = function( node = $( JSONedtr.outputElement + ' > .jse--row > input' ) ){
		return JSON.stringify( JSONedtr.getData() );
	}

	JSONedtr.addRowForm = function( plus ) {
		var lvl = $( plus ).data('level');
		//
		// TODO: add support for array, reference and number
		//
		// var typeofHTML = '<select class="typeof">'+
		// 					'<option value="text" selected="selected">Text</option>'+
		// 					'<option value="object">Object</option>'+
		// 					'<option value="array">Array</option>'+
		// 					'<option value="reference">Reference</option>'+
		// 					'<option value="boolean">Boolean</option>'+
		// 					'<option value="number">Number</option>'+
		// 				'</select>';
		//

		var typeofHTML = '<select class="jse--typeof">'+
							'<option value="text" selected="selected">Text, Number</option>'+
							'<option value="object">Object, Array</option>'+
							'<option value="boolean">Boolean</option>'+
						'</select>';

		$( plus ).html('<input type="text" class="jse--key" data-level="' + lvl + '" value=""> : <span class="jse--typeof">( ' + typeofHTML + ' )</span><input type="text" class="jse--value jse--value__new" value=""><button class="jse--save">Save</button><button class="jse--cancel">Cancel</button>');
		$( plus ).children('.jse--key').focus();

		$( plus ).find( 'select.jse--typeof' ).change(function(){
			switch ( $(this).val() ) {
				case 'text':
					$(this).parent().siblings( '.jse--value__new' ).replaceWith( '<input type="text" class="jse--value jse--value__new" value="">' );
					$(this).parent().siblings( '.jse--value__new' ).focus();
					break;
				case 'boolean':
					$(this).parent().siblings( '.jse--value__new' ).replaceWith( '<input type="checkbox" class="jse--value jse--value__new" value="">' );
					$(this).parent().siblings( '.jse--value__new' ).focus();
					break;
				case 'object':
					$(this).parent().siblings( '.jse--value__new' ).replaceWith( '<span class="jse--value__new"></span>' );
					break;
			}
		})

		$( '.jse--row.jse--add .jse--save' ).click(function( e ){
			JSONedtr.addRow( e.currentTarget.parentElement )
		})

		$( '.jse--row.jse--add .jse--cancel' ).click(function( e ){
			var x = e.currentTarget.parentElement
			$( e.currentTarget.parentElement ).html('<button class="jse--plus">✚</button>');
			$( x ).find( '.jse--plus' ).click( function(e){
				JSONedtr.addRowForm( e.currentTarget.parentElement );
			});
		})
	}

	JSONedtr.addRow = function( row ) {

		var typeOf = $( row ).find( 'select.jse--typeof option:selected' ).val();
		var ii = $( JSONedtr.outputElement ).data('i');
		ii++;
		$( JSONedtr.outputElement ).data('i', ii);
		var lvl = $( row ).data('level');
		$( row ).removeClass( 'jse--add' ).attr('id', 'jse--row-' + ii );
		$( row ).find( 'span.jse--typeof' ).html('(' + typeOf +')');
		var key = $( row ).find( '.jse--key' ).val()
		switch ( typeOf ) {
			case 'text':
				$( row ).find( '.jse--value__new' ).data( 'key', key ).removeClass( 'jse--value__new' );
				break;
			case 'boolean':
				if ($( row ).find( '.jse--value__new' ).is(':checked')) {
					$( row ).find( '.jse--value__new' ).replaceWith( '<input type="text" class="jse--value" value="true" data-key="' + key + '">' );
				} else {
					$( row ).find( '.jse--value__new' ).replaceWith( '<input type="text" class="jse--value" value="false" data-key="' + key + '">' );
				}
				break;
			case 'object':
				$( row ).find( '.jse--key' ).addClass( 'jse--object' );
				$( row ).append( '<div class="xxx jse--row jse--add" data-level="' + (lvl + 1) + '"><button class="jse--plus">✚</button></div>' );
				$( row ).addClass( 'jse--row-object' );
				break;
		}

		$( row ).append( '<div class="jse--delete">✖</div>' );

		$( row ).find( '.jse--delete' ).click(function( e ){
			JSONedtr.deleteRow( e.currentTarget.parentElement );
		})

		$( row ).children( '.jse--save, .jse--cancel' ).remove();
		$( row ).after( '<div class="jse--row jse--add" data-level="' + lvl + '"><button class="jse--plus">✚</button></div>' );
		$( row ).parent().find( '.jse--row.jse--add .jse--plus' ).click( function(e){ JSONedtr.addRowForm( e.currentTarget.parentElement ) });

		$( row ).find( 'input' ).on( 'change input', function( e ){
			if ( JSONedtr.config.runFunctionOnUpdate ) {
				if( JSONedtr.config.instantChange || 'change' == e.type )
					JSONedtr.executeFunctionByName( JSONedtr.config.runFunctionOnUpdate , window, JSONedtr);
			}
		});

		if ( JSONedtr.config.runFunctionOnUpdate ) {
			JSONedtr.executeFunctionByName( JSONedtr.config.runFunctionOnUpdate , window, JSONedtr);
		}

	}

	JSONedtr.deleteRow = function( row ) {
		$( row ).remove();
		if ( JSONedtr.config.runFunctionOnUpdate ) {
			JSONedtr.executeFunctionByName( JSONedtr.config.runFunctionOnUpdate , window, JSONedtr);
		}
	}

	JSONedtr.executeFunctionByName = function(functionName, context /*, args */) {
		var args = Array.prototype.slice.call(arguments, 2);
		var namespaces = functionName.split(".");
		var func = namespaces.pop();
		for(var i = 0; i < namespaces.length; i++) {
			context = context[namespaces[i]];
		}
		return context[func].apply(context, args);
	}

	JSONedtr.init = function( data, outputElement ) {
		data = JSON.parse( data );
		JSONedtr.i = 0;
		JSONedtr.outputElement = outputElement;
		var html = JSONedtr.level( data );

		$( outputElement ).addClass('jse--output').html( html ).data('i', JSONedtr.i);

		$( outputElement + ' .jse--row.jse--add .jse--plus' ).click(function( e ){
			JSONedtr.addRowForm( e.currentTarget.parentElement );
		})

		$( outputElement + ' .jse--row .jse--delete' ).click(function( e ){
			JSONedtr.deleteRow( e.currentTarget.parentElement );
		})

		$( outputElement + ' .jse--row input' ).on( 'change input', function( e ){
			if ( JSONedtr.config.runFunctionOnUpdate ) {
				if( JSONedtr.config.instantChange || 'change' == e.type )
					JSONedtr.executeFunctionByName( JSONedtr.config.runFunctionOnUpdate , window, JSONedtr);
			}
		});
	}

	JSONedtr.init( data, outputElement );

	return JSONedtr;
};
